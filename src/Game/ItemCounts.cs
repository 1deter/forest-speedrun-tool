using System.Collections.Generic;
using ForestOverlay.Data;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Item count sources for trigger evaluation.
    //
    // Live counts come straight from the inventory; the baseline is a
    // snapshot taken when a run starts (Data/RunTimer's ItemSnapshot), so a
    // relative trigger ("gain 3 more rope") can be measured from where you
    // actually began rather than from zero.
    // ------------------------------------------------------------------
    public sealed class LiveItemCounts : IItemCounts
    {
        private readonly InventoryReader _inventory;

        public LiveItemCounts(InventoryReader inventory)
        {
            _inventory = inventory;
        }

        public int AmountOf(int itemId)
        {
            if (_inventory == null) return 0;

            IList<ItemStack> stacks = _inventory.Stacks;
            int total = 0;

            for (int i = 0; i < stacks.Count; i++)
                if (stacks[i].Id == itemId) total += stacks[i].Amount;

            return total;
        }
    }
}
