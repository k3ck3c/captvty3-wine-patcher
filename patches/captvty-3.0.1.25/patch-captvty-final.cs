using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class PatchCaptvtyFinal
{
    static TypeDefinition FindType(ModuleDefinition m, string n)
    {
        foreach (var t in m.Types)
        {
            var r = FindType(t, n);
            if (r != null) return r;
        }
        return null;
    }

    static TypeDefinition FindType(TypeDefinition t, string n)
    {
        if (t.FullName == n) return t;
        foreach (var x in t.NestedTypes)
        {
            var r = FindType(x, n);
            if (r != null) return r;
        }
        return null;
    }

    static MethodDefinition ColorSetter(TypeDefinition t, string n)
    {
        var m = t.Methods.FirstOrDefault(x =>
            x.Name == n && x.IsStatic &&
            x.Parameters.Count == 1 &&
            x.Parameters[0].ParameterType.FullName == "System.Drawing.Color" &&
            x.ReturnType.FullName == "System.Void");
        if (m == null) throw new Exception(t.FullName + "::" + n + " introuvable");
        return m;
    }

    static MethodDefinition SysColorGetter(TypeDefinition t, string n)
    {
        var m = t.Methods.FirstOrDefault(x =>
            x.Name == "get_" + n && x.IsStatic &&
            x.Parameters.Count == 0 &&
            x.ReturnType.FullName == "System.Drawing.Color");
        if (m == null) throw new Exception("SystemColors." + n + " introuvable");
        return m;
    }

    static void PatchFallbackColors(ModuleDefinition mod)
    {
        var npA = FindType(mod, "_npA");
        var colors = FindType(mod, "_npA/_5m");
        if (npA == null || colors == null) throw new Exception("_npA/_5m introuvable");

        var m = npA.Methods.FirstOrDefault(x =>
            x.Name == "_5t" && x.IsStatic &&
            x.Parameters.Count == 0 &&
            x.ReturnType.FullName == "System.Void");
        if (m == null) throw new Exception("_npA::_5t introuvable");

        var setSIA = mod.ImportReference(ColorSetter(colors, "_R9"));
        var set5kb = mod.ImportReference(ColorSetter(colors, "_IxA"));
        var set5W  = mod.ImportReference(ColorSetter(colors, "_ET"));
        var setRwb = mod.ImportReference(ColorSetter(colors, "_zG"));
        var setG   = mod.ImportReference(ColorSetter(colors, "_ir"));

        var sc = mod.ImportReference(typeof(System.Drawing.SystemColors)).Resolve();
        var getWindow = mod.ImportReference(SysColorGetter(sc, "Window"));
        var getHighlight = mod.ImportReference(SysColorGetter(sc, "Highlight"));
        var getHighlightText = mod.ImportReference(SysColorGetter(sc, "HighlightText"));
        var getControlText = mod.ImportReference(SysColorGetter(sc, "ControlText"));
        var getBlack = mod.ImportReference(typeof(System.Drawing.Color).GetProperty("Black").GetGetMethod());

        m.Body.ExceptionHandlers.Clear();
        m.Body.Variables.Clear();
        m.Body.Instructions.Clear();
        var il = m.Body.GetILProcessor();

        il.Append(il.Create(OpCodes.Call, getWindow));
        il.Append(il.Create(OpCodes.Call, setSIA));
        il.Append(il.Create(OpCodes.Call, getHighlight));
        il.Append(il.Create(OpCodes.Call, set5kb));
        il.Append(il.Create(OpCodes.Call, getHighlightText));
        il.Append(il.Create(OpCodes.Call, set5W));
        il.Append(il.Create(OpCodes.Call, getControlText));
        il.Append(il.Create(OpCodes.Call, setRwb));
        il.Append(il.Create(OpCodes.Call, getBlack));
        il.Append(il.Create(OpCodes.Call, setG));
        il.Append(il.Create(OpCodes.Ret));
        m.Body.MaxStackSize = 1;
        m.Body.InitLocals = false;

        Console.WriteLine("[1/5] couleurs de secours");
    }

    static void PatchClamp7s(ModuleDefinition mod)
    {
        var t = mod.Types.FirstOrDefault(x => x.Name == "_eeB");
        if (t == null) throw new Exception("_eeB introuvable");
        var m = t.Methods.FirstOrDefault(x =>
            x.Name == "_7s" && x.HasBody &&
            x.Parameters.Count == 5 &&
            x.ReturnType.FullName == "System.Int32");
        if (m == null) throw new Exception("_eeB::_7s introuvable");

        var min = mod.ImportReference(typeof(Math).GetMethod("Min", new Type[]{typeof(int), typeof(int)}));
        var max = mod.ImportReference(typeof(Math).GetMethod("Max", new Type[]{typeof(int), typeof(int)}));
        var il = m.Body.GetILProcessor();
        var rets = m.Body.Instructions.Where(x => x.OpCode == OpCodes.Ret).ToArray();

        foreach (var ret in rets)
        {
            il.InsertBefore(ret, il.Create(OpCodes.Ldc_I4, 255));
            il.InsertBefore(ret, il.Create(OpCodes.Call, min));
            il.InsertBefore(ret, il.Create(OpCodes.Ldc_I4_0));
            il.InsertBefore(ret, il.Create(OpCodes.Call, max));
        }
        Console.WriteLine("[2/5] clamp _7s 0..255");
    }

    static MethodDefinition F6b(ModuleDefinition mod)
    {
        var t = mod.Types.FirstOrDefault(x => x.Name == "_qzb");
        if (t == null) throw new Exception("_qzb introuvable");
        var m = t.Methods.FirstOrDefault(x => x.Name == "_F6b" && x.HasBody);
        if (m == null) throw new Exception("_qzb::_F6b introuvable");
        return m;
    }

    static void PatchRotate(ModuleDefinition mod)
    {
        var m = F6b(mod);
        var call = m.Body.Instructions.FirstOrDefault(i =>
        {
            var mr = i.Operand as MethodReference;
            return i.OpCode == OpCodes.Callvirt &&
                   mr != null &&
                   mr.Name == "RotateTransform" &&
                   mr.DeclaringType.FullName == "System.Drawing.Drawing2D.LinearGradientBrush" &&
                   mr.Parameters.Count == 1;
        });
        if (call == null) throw new Exception("RotateTransform introuvable");

        var angle = call.Previous;
        var brush = angle != null ? angle.Previous : null;
        if (angle == null || brush == null || angle.OpCode != OpCodes.Ldc_R4)
            throw new Exception("Séquence RotateTransform inattendue");

        var il = m.Body.GetILProcessor();
        il.Remove(call);
        il.Remove(angle);
        il.Remove(brush);
        Console.WriteLine("[3/5] RotateTransform neutralisé");
    }

    static void PatchTranslate(ModuleDefinition mod)
    {
        var m = F6b(mod);
        var call = m.Body.Instructions.FirstOrDefault(i =>
        {
            var mr = i.Operand as MethodReference;
            return i.OpCode == OpCodes.Callvirt &&
                   mr != null &&
                   mr.Name == "TranslateTransform" &&
                   mr.DeclaringType.FullName == "System.Drawing.Drawing2D.LinearGradientBrush" &&
                   mr.Parameters.Count == 2;
        });
        if (call == null) throw new Exception("TranslateTransform introuvable");

        var p4 = call.Previous;
        var p3 = p4 != null ? p4.Previous : null;
        var p2 = p3 != null ? p3.Previous : null;
        var p1 = p2 != null ? p2.Previous : null;

        if (p1 == null || p2 == null || p3 == null || p4 == null)
            throw new Exception("Séquence TranslateTransform incomplète");
        if (p3.OpCode != OpCodes.Conv_R4 || p4.OpCode != OpCodes.Ldc_R4)
            throw new Exception("Séquence TranslateTransform inattendue");

        var il = m.Body.GetILProcessor();
        il.Remove(call);
        il.Remove(p4);
        il.Remove(p3);
        il.Remove(p2);
        il.Remove(p1);
        Console.WriteLine("[4/5] TranslateTransform neutralisé");
    }

    static void PatchLybForeColor(ModuleDefinition mod)
    {
        var qj = mod.Types.First(x => x.Name == "_Qj");
        var lyb = qj.Methods.First(x => x.Name == "_LYb" && x.HasBody);
        var npA = mod.Types.First(x => x.Name == "_npA");
        var colors = npA.NestedTypes.First(x => x.Name == "_5m");
        var iub = colors.Methods.First(x => x.Name == "_iuB" && x.Parameters.Count == 0);
        var iubRef = mod.ImportReference(iub);

        Instruction target = null;
        foreach (var i in lyb.Body.Instructions)
        {
            var mr = i.Operand as MethodReference;
            if (mr != null && mr.Name == "_bTA" && mr.DeclaringType.FullName == "_npA/_5m")
            {
                var next = i.Next;
                var nmr = next != null ? next.Operand as MethodReference : null;
                if (nmr != null && nmr.Name == "set_ForeColor")
                {
                    target = i;
                    break;
                }
            }
        }
        if (target == null) throw new Exception("_bTA/set_ForeColor introuvable dans _Qj::_LYb");

        target.OpCode = OpCodes.Call;
        target.Operand = iubRef;
        Console.WriteLine("[5/5] _Qj::_LYb ForeColor -> _iuB()");
    }

    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: patch-captvty-final.exe input.exe output.exe");
            return 2;
        }

        try
        {
            using (var asm = AssemblyDefinition.ReadAssembly(
                args[0],
                new ReaderParameters { InMemory = true, ReadWrite = false }))
            {
                var mod = asm.MainModule;
                PatchFallbackColors(mod);
                PatchClamp7s(mod);
                PatchRotate(mod);
                PatchTranslate(mod);
                PatchLybForeColor(mod);
                asm.Write(args[1]);
            }

            Console.WriteLine("Patch final appliqué.");
            Console.WriteLine("Fichier produit : " + args[1]);
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.GetType().Name + ": " + e.Message);
            return 1;
        }
    }
}

