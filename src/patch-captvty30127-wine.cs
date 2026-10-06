using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

class PatchCaptvty30127Wine
{
    static void Fail(string s)
    {
        throw new InvalidOperationException(s);
    }

    static MethodDefinition Method(TypeDefinition t, string name)
    {
        return t == null ? null :
            t.Methods.FirstOrDefault(m => m.Name == name);
    }

    static int Calls(MethodDefinition m, string type, string name)
    {
        if (m == null || !m.HasBody) return 0;

        return m.Body.Instructions.Count(i => {
            var mr = i.Operand as MethodReference;
            return mr != null &&
                   mr.DeclaringType.FullName == type &&
                   mr.Name == name;
        });
    }

    static int Main(string[] a)
    {
        if (a.Length != 2) {
            Console.Error.WriteLine(
                "usage: patch-captvty30127-wine.exe input.exe output.exe");
            return 2;
        }

        try {
            var asm = AssemblyDefinition.ReadAssembly(a[0]);
            var mod = asm.MainModule;

            var bza = mod.GetType("_BZA");
            var cva = mod.GetType("_cvA");
            var g8b = mod.GetType("_g8b");
            var rya = mod.GetType("_ryA");
            var xvb = mod.GetType("_XVB");

            if (bza == null || cva == null || g8b == null ||
                rya == null || xvb == null)
                Fail("types attendus de Captvty 3.0.1.27 introuvables");

            var pua = Method(bza, "_PuA");
            var sea = Method(bza, "_SeA");

            var psb = bza.NestedTypes.FirstOrDefault(t => t.Name == "_psB");
            var mzb = Method(psb, "_mZb");

            var cctor = Method(cva, ".cctor");
            var f9za = cva.Fields.FirstOrDefault(f => f.Name == "_9ZA");
            var g8ctor = g8b.Methods.FirstOrDefault(m =>
                m.Name == ".ctor" && !m.HasParameters);

            var gradient = Method(rya, "_0Ib");
            var lab = Method(xvb, "_LAb");

            if (pua == null || sea == null || mzb == null ||
                cctor == null || f9za == null || g8ctor == null ||
                gradient == null || lab == null)
                Fail("membres attendus de Captvty 3.0.1.27 introuvables");

            /*
             * VERIFY #1 -- UXTheme hook.
             */
            if (Calls(pua, "_mYA", "_Rib") < 1)
                Fail("_BZA::_PuA: signature UXTheme inattendue");

            /*
             * VERIFY #2 -- visual style colour initialisation.
             */
            if (Calls(sea,
                      "System.Windows.Forms.VisualStyles.VisualStyleRenderer",
                      ".ctor") != 5 ||
                Calls(sea,
                      "System.Windows.Forms.VisualStyles.VisualStyleRenderer",
                      "GetColor") != 5 ||
                Calls(sea, "_BZA/_psB", "_mZb") != 1)
                Fail("_BZA::_SeA: signature VisualStyle inattendue");

            /*
             * VERIFY #3 -- imageres icon initialisation.
             */
            if (Calls(cctor, "_9Zb", "_9r") != 2 ||
                Calls(cctor, "System.Drawing.Icon", "FromHandle") != 2 ||
                Calls(cctor, "_g8b", ".ctor") != 1)
                Fail("_cvA::.cctor: signature icones inattendue");

            /*
             * VERIFY #4 -- LinearGradientBrush.RotateTransform(20f).
             */
            var gi = gradient.Body.Instructions;
            int rotateIndex = -1;
            int rotateCount = 0;

            for (int i = 2; i < gi.Count; i++) {
                var mr = gi[i].Operand as MethodReference;

                if (mr != null &&
                    mr.DeclaringType.FullName ==
                        "System.Drawing.Drawing2D.LinearGradientBrush" &&
                    mr.Name == "RotateTransform" &&
                    mr.Parameters.Count == 1 &&
                    mr.Parameters[0].ParameterType.FullName ==
                        "System.Single") {
                    rotateIndex = i;
                    rotateCount++;
                }
            }

            if (rotateCount != 1)
                Fail("_ryA::_0Ib: RotateTransform attendu 1, trouve " +
                     rotateCount);

            if (gi[rotateIndex - 2].OpCode != OpCodes.Ldloc_1 ||
                gi[rotateIndex - 1].OpCode != OpCodes.Ldc_R4 ||
                !(gi[rotateIndex - 1].Operand is float) ||
                (float)gi[rotateIndex - 1].Operand != 20.0f)
                Fail("_ryA::_0Ib: signature RotateTransform(20f) inattendue");

            /*
             * VERIFY #5 -- download pane height.
             */
            var li = lab.Body.Instructions;
            int heightIndex = -1;
            int heightCount = 0;

            for (int i = 0; i + 2 < li.Count; i++) {
                if (li[i].OpCode != OpCodes.Ldc_I4 ||
                    !(li[i].Operand is int) ||
                    (int)li[i].Operand != 300)
                    continue;

                var m1 = li[i + 1].Operand as MethodReference;
                var m2 = li[i + 2].Operand as MethodReference;

                if (m1 != null && m2 != null &&
                    m1.DeclaringType.FullName == "_BZA/_QU" &&
                    m1.Name == "_zk" &&
                    m2.Name == "set_Height") {
                    heightIndex = i;
                    heightCount++;
                }
            }

            if (heightCount != 1)
                Fail("_XVB::_LAb: hauteur cible attendue 1, trouve " +
                     heightCount);

            var drawing = mod.AssemblyReferences
                .FirstOrDefault(r => r.Name == "System.Drawing");

            if (drawing == null)
                Fail("reference System.Drawing introuvable");

            Console.WriteLine("VERIFY OK: Captvty 3.0.1.27 reconnu");

            /*
             * PATCH #1
             */
            pua.Body.Instructions.Clear();
            pua.Body.ExceptionHandlers.Clear();
            pua.Body.Variables.Clear();
            pua.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

            /*
             * PATCH #2
             */
            var systemColors = new TypeReference(
                "System.Drawing", "SystemColors",
                mod, drawing, false);

            var color = new TypeReference(
                "System.Drawing", "Color",
                mod, drawing, true);

            var getWindow = new MethodReference(
                "get_Window", color, systemColors) {
                    HasThis = false
                };

            sea.Body.Instructions.Clear();
            sea.Body.ExceptionHandlers.Clear();
            sea.Body.Variables.Clear();

            var sil = sea.Body.GetILProcessor();
            sil.Append(Instruction.Create(
                OpCodes.Call, mod.ImportReference(getWindow)));
            sil.Append(Instruction.Create(OpCodes.Call, mzb));
            sil.Append(Instruction.Create(OpCodes.Ret));

            /*
             * PATCH #3
             */
            cctor.Body.ExceptionHandlers.Clear();
            cctor.Body.Variables.Clear();
            cctor.Body.Instructions.Clear();

            var cil = cctor.Body.GetILProcessor();
            cil.Append(cil.Create(OpCodes.Newobj, g8ctor));
            cil.Append(cil.Create(OpCodes.Stsfld, f9za));
            cil.Append(cil.Create(OpCodes.Ret));

            /*
             * PATCH #4
             */
            for (int j = rotateIndex - 2; j <= rotateIndex; j++) {
                gi[j].OpCode = OpCodes.Nop;
                gi[j].Operand = null;
            }

            /*
             * PATCH #5
             */
            li[heightIndex].Operand = 150;

            asm.Write(a[1]);

            Console.WriteLine("PATCH 1/5 OK: UXTheme hook");
            Console.WriteLine("PATCH 2/5 OK: VisualStyle fallback");
            Console.WriteLine("PATCH 3/5 OK: imageres icon path");
            Console.WriteLine("PATCH 4/5 OK: gradient RotateTransform");
            Console.WriteLine("PATCH 5/5 OK: download height 300 -> 150");
            Console.WriteLine("OUTPUT: " + a[1]);

            return 0;
        }
        catch (Exception e) {
            Console.Error.WriteLine("ABORT: " + e.Message);
            return 1;
        }
    }
}
