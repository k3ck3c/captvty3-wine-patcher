using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

/*
 * Patch Captvty 3.0.1.24 pour Wine.
 *
 * Corrections appliquées :
 *
 *  1. _zcA::_UXB()
 *     Neutralise un mécanisme de hooks de DLL qui échoue sous Wine.
 *
 *  2. _zcA::_5tb()
 *     Remplace l'interrogation des Visual Styles Windows par des
 *     couleurs SystemColors valides.
 *
 *  3. _T0A::_h6b()
 *     Évite FileInfo.Length lorsque le fichier temporaire n'existe pas.
 *
 *  4. _T0A::_dYb()
 *     Évite Stream.Position lorsque le flux n'est pas encore initialisé.
 *
 *  5. _xVA/_sSb::.ctor(...)
 *     Évite FileInfo.Length lorsque le fichier vient d'être renommé
 *     ou n'existe plus.
 *
 * Ce programme doit être appliqué au Captvty.exe ORIGINAL.
 */

internal static class PatchCaptvty30124Wine
{
    private static IEnumerable<TypeDefinition> AllTypes(
        IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;

            foreach (var nested in AllTypes(type.NestedTypes))
                yield return nested;
        }
    }

    private static TypeDefinition FindType(
        ModuleDefinition module,
        string name)
    {
        return AllTypes(module.Types)
            .SingleOrDefault(t =>
                t.Name == name ||
                t.FullName == name);
    }

    private static MethodDefinition FindMethod(
        TypeDefinition type,
        string name,
        string returnType,
        int parameterCount)
    {
        return type.Methods.SingleOrDefault(m =>
            m.Name == name &&
            m.ReturnType.FullName == returnType &&
            m.Parameters.Count == parameterCount);
    }

    private static FieldDefinition FindField(
        TypeDefinition type,
        string name,
        string fieldType)
    {
        return type.Fields.SingleOrDefault(f =>
            f.Name == name &&
            f.FieldType.FullName == fieldType);
    }

    private static void ClearBody(MethodDefinition method)
    {
        if (!method.HasBody)
            throw new InvalidOperationException(
                method.FullName + " n'a pas de corps IL.");

        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.InitLocals = false;
    }

    /*
     * Patch 1 :
     *
     * _zcA::_UXB()
     *
     * Devient simplement :
     *
     *     ret
     */
    private static void PatchDisableUxb(TypeDefinition zca)
    {
        var method = FindMethod(
            zca,
            "_UXB",
            "System.Void",
            0);

        if (method == null)
            throw new InvalidOperationException(
                "Méthode _zcA::_UXB() introuvable.");

        ClearBody(method);

        var il = method.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ret));
    }

    private static MethodDefinition FindColorSetter(
        TypeDefinition type,
        string name)
    {
        return type.Methods.SingleOrDefault(m =>
            m.Name == name &&
            m.IsStatic &&
            m.ReturnType.FullName == "System.Void" &&
            m.Parameters.Count == 1 &&
            m.Parameters[0].ParameterType.FullName ==
                "System.Drawing.Color");
    }

    /*
     * Patch 2 :
     *
     * _zcA::_5tb()
     *
     * L'original interroge VisualStyleRenderer avec des classes de thème
     * que Wine ne reconnaît pas toujours.
     *
     * On initialise les couleurs internes avec :
     *
     *     SystemColors.Window
     *     SystemColors.WindowText
     */
    private static void PatchThemeColors(
        ModuleDefinition module,
        TypeDefinition zca)
    {
        var method = FindMethod(
            zca,
            "_5tb",
            "System.Void",
            0);

        if (method == null)
            throw new InvalidOperationException(
                "Méthode _zcA::_5tb() introuvable.");

        var colorStore = zca.NestedTypes.SingleOrDefault(
            t => t.Name == "_vSb");

        if (colorStore == null)
            throw new InvalidOperationException(
                "Type _zcA/_vSb introuvable.");

        var setBackground = FindColorSetter(colorStore, "_ltB");
        var setText1 = FindColorSetter(colorStore, "_eA");
        var setText2 = FindColorSetter(colorStore, "_XwA");
        var setText3 = FindColorSetter(colorStore, "_dXB");
        var setText4 = FindColorSetter(colorStore, "_YKb");

        if (setBackground == null ||
            setText1 == null ||
            setText2 == null ||
            setText3 == null ||
            setText4 == null)
        {
            throw new InvalidOperationException(
                "Un setter de couleur de _vSb est introuvable.");
        }

        var getWindow = typeof(System.Drawing.SystemColors)
            .GetProperty("Window")
            ?.GetGetMethod();

        var getWindowText = typeof(System.Drawing.SystemColors)
            .GetProperty("WindowText")
            ?.GetGetMethod();

        if (getWindow == null || getWindowText == null)
            throw new InvalidOperationException(
                "Getters SystemColors introuvables.");

        ClearBody(method);

        var il = method.Body.GetILProcessor();

        // Couleur de fond principale.
        il.Append(il.Create(
            OpCodes.Call,
            module.ImportReference(getWindow)));

        il.Append(il.Create(
            OpCodes.Call,
            module.ImportReference(setBackground)));

        // Les quatre autres valeurs servent de couleurs de texte.
        foreach (var setter in new[]
        {
            setText1,
            setText2,
            setText3,
            setText4
        })
        {
            il.Append(il.Create(
                OpCodes.Call,
                module.ImportReference(getWindowText)));

            il.Append(il.Create(
                OpCodes.Call,
                module.ImportReference(setter)));
        }

        il.Append(il.Create(OpCodes.Ret));
    }

    /*
     * Patch 3 :
     *
     * Dans le gestionnaire de secours de _T0A::_h6b(), l'original fait :
     *
     *     new FileInfo(_vKB).Length
     *
     * sans vérifier que le fichier existe.
     *
     * On insère :
     *
     *     if (!File.Exists(_vKB))
     *         longueur = 0;
     */
    private static void PatchTemporaryFileLength(
        ModuleDefinition module,
        TypeDefinition t0a)
    {
        var method = FindMethod(
            t0a,
            "_h6b",
            "System.Int64",
            0);

        if (method == null || !method.HasBody)
            throw new InvalidOperationException(
                "Méthode _T0A::_h6b() introuvable.");

        var pathField = FindField(
            t0a,
            "_vKB",
            "System.String");

        if (pathField == null)
            throw new InvalidOperationException(
                "Champ _T0A::_vKB introuvable.");

        var instructions = method.Body.Instructions;

        var fileInfoCtor = instructions.FirstOrDefault(i =>
            i.OpCode == OpCodes.Newobj &&
            i.Operand is MethodReference mr &&
            mr.DeclaringType.FullName == "System.IO.FileInfo" &&
            mr.Name == ".ctor");

        if (fileInfoCtor == null)
            throw new InvalidOperationException(
                "FileInfo::.ctor introuvable dans _h6b().");

        int ctorIndex = instructions.IndexOf(fileInfoCtor);

        if (ctorIndex < 2)
            throw new InvalidOperationException(
                "Séquence FileInfo inattendue dans _h6b().");

        /*
         * Bloc original :
         *
         *   ldarg.0
         *   ldfld _vKB
         *   newobj FileInfo::.ctor
         */
        var fallbackStart = instructions[ctorIndex - 2];

        var existingLeave = instructions
            .Skip(ctorIndex)
            .FirstOrDefault(i =>
                i.OpCode == OpCodes.Leave ||
                i.OpCode == OpCodes.Leave_S);

        if (existingLeave == null ||
            !(existingLeave.Operand is Instruction leaveTarget))
        {
            throw new InvalidOperationException(
                "Destination leave introuvable dans _h6b().");
        }

        var fileExistsMethod = typeof(File).GetMethod(
            "Exists",
            new[] { typeof(string) });

        if (fileExistsMethod == null)
            throw new InvalidOperationException(
                "File.Exists introuvable.");

        var fileExists = module.ImportReference(fileExistsMethod);
        var il = method.Body.GetILProcessor();

        il.InsertBefore(
            fallbackStart,
            il.Create(OpCodes.Ldarg_0));

        il.InsertBefore(
            fallbackStart,
            il.Create(OpCodes.Ldfld, pathField));

        il.InsertBefore(
            fallbackStart,
            il.Create(OpCodes.Call, fileExists));

        // Si le fichier existe, continuer dans le bloc original.
        il.InsertBefore(
            fallbackStart,
            il.Create(OpCodes.Brtrue, fallbackStart));

        // Sinon, retourner une longueur nulle via le retour commun.
        il.InsertBefore(
            fallbackStart,
            il.Create(OpCodes.Ldc_I4_0));

        il.InsertBefore(
            fallbackStart,
            il.Create(OpCodes.Conv_I8));

        il.InsertBefore(
            fallbackStart,
            il.Create(OpCodes.Stloc_0));

        il.InsertBefore(
            fallbackStart,
            il.Create(OpCodes.Leave, leaveTarget));
    }

    /*
     * Patch 4 :
     *
     * _T0A::_dYb() fait normalement :
     *
     *     return _30().Position;
     *
     * Sous Wine, _30() peut momentanément renvoyer null.
     *
     * On remplace le corps par :
     *
     *     Stream s = _30();
     *     return s == null ? 0L : s.Position;
     */
    private static void PatchStreamPosition(
        ModuleDefinition module,
        TypeDefinition t0a)
    {
        var method = FindMethod(
            t0a,
            "_dYb",
            "System.Int64",
            0);

        if (method == null)
            throw new InvalidOperationException(
                "Méthode _T0A::_dYb() introuvable.");

        var streamGetter = FindMethod(
            t0a,
            "_30",
            "System.IO.Stream",
            0);

        if (streamGetter == null)
            throw new InvalidOperationException(
                "Méthode _T0A::_30() introuvable.");

        var positionGetter = typeof(Stream)
            .GetProperty("Position")
            ?.GetGetMethod();

        if (positionGetter == null)
            throw new InvalidOperationException(
                "Stream.Position introuvable.");

        ClearBody(method);

        var il = method.Body.GetILProcessor();
        var streamIsValid = il.Create(OpCodes.Nop);

        il.Append(il.Create(OpCodes.Ldarg_0));

        il.Append(il.Create(
            OpCodes.Call,
            module.ImportReference(streamGetter)));

        /*
         * Une copie reste sur la pile si le flux est valide.
         */
        il.Append(il.Create(OpCodes.Dup));
        il.Append(il.Create(OpCodes.Brtrue_S, streamIsValid));

        // Flux null : retirer null de la pile et retourner 0.
        il.Append(il.Create(OpCodes.Pop));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Conv_I8));
        il.Append(il.Create(OpCodes.Ret));

        // Flux valide : lire Position.
        il.Append(streamIsValid);

        il.Append(il.Create(
            OpCodes.Callvirt,
            module.ImportReference(positionGetter)));

        il.Append(il.Create(OpCodes.Ret));
    }

    /*
     * Patch 5 :
     *
     * Le constructeur _xVA/_sSb::.ctor(...) stocke un FileInfo puis lit
     * immédiatement Length.
     *
     * À la fin du téléchargement, le fichier peut déjà avoir été renommé.
     *
     * On insère :
     *
     *     if (!File.Exists(arg2))
     *         _0MB = 0;
     *     else
     *         _0MB = _hU.Length;
     */
    private static void PatchFinalFileLength(
        ModuleDefinition module,
        TypeDefinition xva)
    {
        var ssb = xva.NestedTypes.SingleOrDefault(
            t => t.Name == "_sSb");

        if (ssb == null)
            throw new InvalidOperationException(
                "Type _xVA/_sSb introuvable.");

        var ctor = ssb.Methods.SingleOrDefault(m =>
            m.Name == ".ctor" &&
            m.Parameters.Count == 3 &&
            m.Parameters[0].ParameterType.FullName == "System.String" &&
            m.Parameters[1].ParameterType.FullName == "System.String" &&
            m.Parameters[2].ParameterType.FullName == "System.Boolean");

        if (ctor == null || !ctor.HasBody)
            throw new InvalidOperationException(
                "Constructeur _xVA/_sSb::.ctor introuvable.");

        var fileInfoField = FindField(
            ssb,
            "_hU",
            "System.IO.FileInfo");

        var lengthField = FindField(
            ssb,
            "_0MB",
            "System.Int64");

        if (fileInfoField == null || lengthField == null)
            throw new InvalidOperationException(
                "Champs _hU ou _0MB introuvables.");

        var instructions = ctor.Body.Instructions;

        var getLength = instructions.FirstOrDefault(i =>
            i.OpCode == OpCodes.Callvirt &&
            i.Operand is MethodReference mr &&
            mr.DeclaringType.FullName == "System.IO.FileInfo" &&
            mr.Name == "get_Length");

        if (getLength == null)
            throw new InvalidOperationException(
                "FileInfo.get_Length introuvable dans _sSb::.ctor.");

        int index = instructions.IndexOf(getLength);

        if (index < 3)
            throw new InvalidOperationException(
                "Séquence FileInfo.Length inattendue.");

        /*
         * Début du bloc original :
         *
         *   ldarg.0
         *   ldarg.0
         *   ldfld _hU
         *   callvirt get_Length
         */
        var originalLengthBlock = instructions[index - 3];

        var ret = instructions.LastOrDefault(
            i => i.OpCode == OpCodes.Ret);

        if (ret == null)
            throw new InvalidOperationException(
                "Instruction ret introuvable.");

        var fileExistsMethod = typeof(File).GetMethod(
            "Exists",
            new[] { typeof(string) });

        if (fileExistsMethod == null)
            throw new InvalidOperationException(
                "File.Exists introuvable.");

        var fileExists = module.ImportReference(fileExistsMethod);
        var il = ctor.Body.GetILProcessor();

        // arg2 est le chemin du fichier.
        il.InsertBefore(
            originalLengthBlock,
            il.Create(OpCodes.Ldarg_2));

        il.InsertBefore(
            originalLengthBlock,
            il.Create(OpCodes.Call, fileExists));

        // Fichier existant : exécuter le bloc original.
        il.InsertBefore(
            originalLengthBlock,
            il.Create(OpCodes.Brtrue, originalLengthBlock));

        // Fichier absent : this._0MB = 0L.
        il.InsertBefore(
            originalLengthBlock,
            il.Create(OpCodes.Ldarg_0));

        il.InsertBefore(
            originalLengthBlock,
            il.Create(OpCodes.Ldc_I4_0));

        il.InsertBefore(
            originalLengthBlock,
            il.Create(OpCodes.Conv_I8));

        il.InsertBefore(
            originalLengthBlock,
            il.Create(OpCodes.Stfld, lengthField));

        il.InsertBefore(
            originalLengthBlock,
            il.Create(OpCodes.Br, ret));
    }

    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: patch-captvty30124-wine.exe " +
                "<Captvty.exe original> <Captvty.exe patché>");
            return 2;
        }

        string input = Path.GetFullPath(args[0]);
        string output = Path.GetFullPath(args[1]);

        if (!File.Exists(input))
        {
            Console.Error.WriteLine(
                "Fichier d'entrée introuvable : " + input);
            return 2;
        }

        if (string.Equals(
            input,
            output,
            StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                "Le fichier de sortie doit être différent de l'original.");
            return 2;
        }

        try
        {
            var assembly = AssemblyDefinition.ReadAssembly(
                input,
                new ReaderParameters
                {
                    InMemory = true,
                    ReadWrite = false
                });

            var module = assembly.MainModule;

            var zca = FindType(module, "_zcA");
            var t0a = FindType(module, "_T0A");
            var xva = FindType(module, "_xVA");

            if (zca == null)
                throw new InvalidOperationException(
                    "Type _zcA introuvable.");

            if (t0a == null)
                throw new InvalidOperationException(
                    "Type _T0A introuvable.");

            if (xva == null)
                throw new InvalidOperationException(
                    "Type _xVA introuvable.");

            Console.WriteLine("Application des correctifs...");

            PatchDisableUxb(zca);
            Console.WriteLine(
                "[OK] _zcA::_UXB() neutralisée");

            PatchThemeColors(module, zca);
            Console.WriteLine(
                "[OK] couleurs de thème remplacées par SystemColors");

            PatchTemporaryFileLength(module, t0a);
            Console.WriteLine(
                "[OK] _T0A::_h6b() protège FileInfo.Length");

            PatchStreamPosition(module, t0a);
            Console.WriteLine(
                "[OK] _T0A::_dYb() protège Stream.Position");

            PatchFinalFileLength(module, xva);
            Console.WriteLine(
                "[OK] _xVA/_sSb::.ctor protège FileInfo.Length");

            assembly.Write(output);

            Console.WriteLine();
            Console.WriteLine("Patch terminé.");
            Console.WriteLine("Entrée : " + input);
            Console.WriteLine("Sortie : " + output);

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "Échec du patch : " +
                ex.GetType().Name +
                ": " +
                ex.Message);

            return 1;
        }
    }
}