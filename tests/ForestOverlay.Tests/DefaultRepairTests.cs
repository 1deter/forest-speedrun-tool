using System.Collections.Generic;
using System.IO;
using ForestOverlay.Updater;
using Mono.Cecil;
using Xunit;

namespace ForestOverlay.Tests
{
    // T-0246: ModAPI's rebuilt Assembly-CSharp has optional parameters with
    // no default value; the game's Mono dies when reflection reads them.
    // The assembly here is built the way ModAPI writes it.
    public class DefaultRepairTests
    {
        private readonly List<string> _log = new List<string>();

        private static ModuleDefinition Broken(bool modApiRef)
        {
            ModuleDefinition module = ModuleDefinition.CreateModule("Assembly-CSharp", ModuleKind.Dll);
            if (modApiRef) module.AssemblyReferences.Add(new AssemblyNameReference(DefaultRepair.ModApiAssembly, new System.Version(1, 0)));
            TypeDefinition t = new TypeDefinition("TheForest.Items.Inventory", "PlayerInventory",
                TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
            module.Types.Add(t);

            MethodDefinition original = Method(module, "__RemoveItem__Original");
            original.Parameters[1].Constant = 1;
            original.Parameters[2].Constant = false;
            original.Parameters[3].Constant = null;
            t.Methods.Add(original);
            t.Methods.Add(Method(module, "RemoveItem"));   // ModAPI's: HasDefault, no constants

            MethodDefinition lone = new MethodDefinition("Lone", MethodAttributes.Public, module.TypeSystem.Void);
            lone.Parameters.Add(new ParameterDefinition("x", ParameterAttributes.Optional | ParameterAttributes.HasDefault, module.TypeSystem.Int32));
            Body(lone);
            t.Methods.Add(lone);
            return module;
        }

        private static MethodDefinition Method(ModuleDefinition module, string name)
        {
            MethodDefinition m = new MethodDefinition(name, MethodAttributes.Public, module.TypeSystem.Boolean);
            const ParameterAttributes opt = ParameterAttributes.Optional | ParameterAttributes.HasDefault;
            m.Parameters.Add(new ParameterDefinition("itemId", ParameterAttributes.None, module.TypeSystem.Int32));
            m.Parameters.Add(new ParameterDefinition("amount", opt, module.TypeSystem.Int32));
            m.Parameters.Add(new ParameterDefinition("allowAmountOverflow", opt, module.TypeSystem.Boolean));
            m.Parameters.Add(new ParameterDefinition("properties", opt, module.TypeSystem.Object));
            Body(m);
            return m;
        }

        private static void Body(MethodDefinition m)
        {
            var il = m.Body.GetILProcessor();
            if (m.ReturnType.MetadataType != MetadataType.Void) il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        }

        private static ModuleDefinition RoundTrip(ModuleDefinition module)
        {
            MemoryStream s = new MemoryStream();
            module.Write(s);
            s.Position = 0;
            return ModuleDefinition.ReadModule(s);
        }

        private static MethodDefinition Find(ModuleDefinition module, string name)
        {
            foreach (MethodDefinition m in module.GetType("TheForest.Items.Inventory.PlayerInventory").Methods)
                if (m.Name == name) return m;
            return null;
        }

        [Fact]
        public void Written_like_ModAPI_the_flag_survives_without_a_constant()
        {
            MethodDefinition m = Find(RoundTrip(Broken(true)), "RemoveItem");
            Assert.True(m.Parameters[1].HasDefault);
            Assert.False(m.Parameters[1].HasConstant);
        }

        [Fact]
        public void Copies_the_defaults_from_the_original_method()
        {
            ModuleDefinition module = RoundTrip(Broken(true));
            Assert.Equal(4, DefaultRepair.Repair(module, _log.Add));   // RemoveItem's 3 + Lone's 1

            MethodDefinition m = Find(RoundTrip(module), "RemoveItem");
            Assert.Equal(1, m.Parameters[1].Constant);
            Assert.Equal(false, m.Parameters[2].Constant);
            Assert.True(m.Parameters[3].HasConstant);
            Assert.Null(m.Parameters[3].Constant);
            Assert.False(m.Parameters[0].HasDefault);
        }

        [Fact]
        public void Clears_the_flag_when_there_is_no_original()
        {
            ModuleDefinition module = RoundTrip(Broken(true));
            DefaultRepair.Repair(module, _log.Add);
            MethodDefinition lone = Find(RoundTrip(module), "Lone");
            Assert.False(lone.Parameters[0].HasDefault);
            Assert.Contains(_log, l => l.Contains("::Lone x: no default to copy"));
        }

        [Fact]
        public void A_sound_assembly_is_left_alone()
        {
            ModuleDefinition module = RoundTrip(Broken(true));
            DefaultRepair.Repair(module, null);
            Assert.Equal(0, DefaultRepair.Repair(RoundTrip(module), _log.Add));
            Assert.Empty(_log);
        }

        [Fact]
        public void Only_ModAPIs_assembly_counts_as_ModAPI()
        {
            Assert.True(DefaultRepair.ReferencesModApi(Broken(true)));
            Assert.False(DefaultRepair.ReferencesModApi(Broken(false)));
        }
    }
}
