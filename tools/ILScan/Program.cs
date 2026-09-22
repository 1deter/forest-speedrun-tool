using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ForestOverlay.ILScan
{
    // Offline IL query tool over the game's Assembly-CSharp.dll.
    //
    //   ilscan refs   <substring>   methods whose IL references a matching member
    //   ilscan writes <substring>   methods that STORE to a matching field/property
    //   ilscan body   <Type::Method>  disassemble matching method bodies
    //   ilscan type   <substring>   members of matching types
    //   ilscan strings <substring>  methods with a matching string literal -
    //                               finds SendMessage / StartCoroutine /
    //                               Invoke by name, which refs cannot see
    //
    // Matching is case-insensitive substring over the full member name.
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("usage: ilscan <refs|writes|body|type|strings> <substring> [--asm <path>] [--max N]");
                return 2;
            }

            string mode = args[0].ToLowerInvariant();
            string needle = args[1];
            string asmPath = GetOpt(args, "--asm") ?? DefaultAssemblyPath();
            int max = int.TryParse(GetOpt(args, "--max"), out int m) ? m : 60;

            if (asmPath == null || !File.Exists(asmPath))
            {
                Console.Error.WriteLine("Assembly-CSharp.dll not found. Pass --asm <path> or set FOREST_MANAGED_PATH.");
                return 3;
            }

            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(asmPath));

            using var asm = AssemblyDefinition.ReadAssembly(
                asmPath, new ReaderParameters { AssemblyResolver = resolver, ReadingMode = ReadingMode.Deferred });

            Console.WriteLine($"# assembly : {Path.GetFileName(asmPath)}");
            Console.WriteLine($"# mode     : {mode}");
            Console.WriteLine($"# needle   : {needle}");
            Console.WriteLine();

            int hits = mode switch
            {
                "refs"   => ScanBodies(asm, needle, max, writesOnly: false),
                "writes" => ScanBodies(asm, needle, max, writesOnly: true),
                "body"   => DumpBodies(asm, needle, max),
                "type"   => DumpTypes(asm, needle, max),
                "strings" => ScanStrings(asm, needle, max),
                _        => -1
            };

            if (hits < 0) { Console.Error.WriteLine("unknown mode: " + mode); return 2; }
            Console.WriteLine();
            Console.WriteLine($"# {hits} match(es)" + (hits >= max ? $" (capped at {max})" : ""));
            return 0;
        }

        private static IEnumerable<TypeDefinition> AllTypes(AssemblyDefinition asm)
        {
            foreach (var module in asm.Modules)
                foreach (var t in module.Types)
                    foreach (var nested in Flatten(t))
                        yield return nested;
        }

        private static IEnumerable<TypeDefinition> Flatten(TypeDefinition t)
        {
            yield return t;
            foreach (var n in t.NestedTypes)
                foreach (var d in Flatten(n))
                    yield return d;
        }

        // Store opcodes: assignment to a field, or a property setter call.
        private static bool IsStore(Instruction ins)
        {
            var c = ins.OpCode.Code;
            if (c is Code.Stfld or Code.Stsfld) return true;
            if (c is Code.Call or Code.Callvirt && ins.Operand is MethodReference mr)
                return mr.Name.StartsWith("set_", StringComparison.Ordinal);
            return false;
        }

        private static string OperandName(Instruction ins) => ins.Operand switch
        {
            FieldReference f  => f.DeclaringType.FullName + "::" + f.Name,
            MethodReference m => m.DeclaringType.FullName + "::" + m.Name,
            _ => null
        };

        private static int ScanBodies(AssemblyDefinition asm, string needle, int max, bool writesOnly)
        {
            int hits = 0;
            foreach (var type in AllTypes(asm))
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody) continue;
                    var seen = new List<string>();

                    foreach (var ins in method.Body.Instructions)
                    {
                        if (writesOnly && !IsStore(ins)) continue;
                        string name = OperandName(ins);
                        if (name == null) continue;
                        if (name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        string entry = (writesOnly ? "STORE " : "") + ins.OpCode.Name + " " + name;
                        if (!seen.Contains(entry)) seen.Add(entry);
                    }

                    if (seen.Count == 0) continue;
                    if (++hits > max) return max;

                    Console.WriteLine($"{type.FullName}::{method.Name}");
                    foreach (var s in seen) Console.WriteLine("    " + s);
                }
            }
            return hits;
        }

        private static int ScanStrings(AssemblyDefinition asm, string needle, int max)
        {
            int hits = 0;
            foreach (var type in AllTypes(asm))
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody) continue;
                    var seen = new List<string>();

                    foreach (var ins in method.Body.Instructions)
                    {
                        if (ins.OpCode.Code != Code.Ldstr || ins.Operand is not string lit) continue;
                        if (lit.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;

                        // The call that consumes the string is usually the
                        // next call instruction - show it for context.
                        string use = "";
                        for (var n = ins.Next; n != null; n = n.Next)
                        {
                            if (n.OpCode.Code is Code.Call or Code.Callvirt) { use = "  -> " + OperandName(n); break; }
                        }
                        string entry = "ldstr \"" + lit + "\"" + use;
                        if (!seen.Contains(entry)) seen.Add(entry);
                    }

                    if (seen.Count == 0) continue;
                    if (++hits > max) return max;

                    Console.WriteLine($"{type.FullName}::{method.Name}");
                    foreach (var e in seen) Console.WriteLine("    " + e);
                }
            }
            return hits;
        }

        private static int DumpBodies(AssemblyDefinition asm, string needle, int max)
        {
            int hits = 0;
            foreach (var type in AllTypes(asm))
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody) continue;
                    string full = type.FullName + "::" + method.Name;
                    if (full.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (++hits > max) return max;

                    Console.WriteLine($"=== {full} ===");
                    foreach (var ins in method.Body.Instructions)
                        Console.WriteLine("    " + ins);
                    Console.WriteLine();
                }
            }
            return hits;
        }

        private static int DumpTypes(AssemblyDefinition asm, string needle, int max)
        {
            int hits = 0;
            foreach (var type in AllTypes(asm))
            {
                if (type.FullName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++hits > max) return max;

                Console.WriteLine($"=== {type.FullName}  :  {type.BaseType?.FullName ?? "-"} ===");
                foreach (var f in type.Fields)
                    Console.WriteLine($"    field  {f.FieldType.Name} {f.Name}{(f.IsStatic ? "  [static]" : "")}");
                foreach (var p in type.Properties)
                    Console.WriteLine($"    prop   {p.PropertyType.Name} {p.Name}");
                foreach (var mm in type.Methods)
                    Console.WriteLine($"    method {mm.ReturnType.Name} {mm.Name}({string.Join(", ", mm.Parameters.Select(x => x.ParameterType.Name + " " + x.Name))})");
                Console.WriteLine();
            }
            return hits;
        }

        private static string GetOpt(string[] args, string key)
        {
            int i = Array.IndexOf(args, key);
            return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : null;
        }

        private static string DefaultAssemblyPath()
        {
            string managed = Environment.GetEnvironmentVariable("FOREST_MANAGED_PATH");
            if (string.IsNullOrEmpty(managed))
            {
                string root = Environment.GetEnvironmentVariable("FOREST_ROOT");
                if (!string.IsNullOrEmpty(root))
                    managed = Path.Combine(root, "TheForest_Data", "Managed");
            }
            return string.IsNullOrEmpty(managed) ? null : Path.Combine(managed, "Assembly-CSharp.dll");
        }
    }
}
