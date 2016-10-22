using SilverSim.Types;
using SilverSim.Types.Inventory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SilverSim.Viewer.AISv3
{
    public static class AISv3ExtensionMethods
    {
        public static Map ToAisV3(this InventoryFolder folder)
        {
            Map resmap = new Map();
            resmap.Add("name", folder.Name);
            resmap.Add("type_default", (int)folder.InventoryType);
            resmap.Add("parent_id", folder.ParentFolderID);
            resmap.Add("version", folder.Version);
            resmap.Add("agent_id", folder.Owner.ID);
            resmap.Add("category_id", folder.ID);
            return resmap;
        }

        public static Map ToAisV3(this InventoryItem item)
        {
            Map resmap = new Map();
            resmap.Add("asset_id", item.AssetID);
            resmap.Add("inv_type", (int)item.InventoryType);
            resmap.Add("name", item.Name);
            Map sale_info = new Map();
            sale_info.Add("sale_price", item.SaleInfo.Price);
            sale_info.Add("sale_type", (int)item.SaleInfo.Type);
            resmap.Add("sale_info", sale_info);
            resmap.Add("created_at", item.CreationDate.DateTimeToUnixTime());
            resmap.Add("parent_id", item.ParentFolderID);
            resmap.Add("flags", (int)item.Flags);
            resmap.Add("agent_id", item.Owner.ID);
            resmap.Add("item_id", item.ID);
            Map perm_info = new Map();
            perm_info.Add("base_mask", (int)item.Permissions.Base);
            perm_info.Add("group_mask", (int)item.Permissions.Group);
            perm_info.Add("last_owner_id", item.LastOwner.ID);
            perm_info.Add("owner_id", item.Owner.ID);
            perm_info.Add("creator_id", item.Creator.ID);
            perm_info.Add("next_owner_mask", (int)item.Permissions.NextOwner);
            perm_info.Add("owner_mask", (int)item.Permissions.Current);
            perm_info.Add("group_id", item.Group.ID);
            perm_info.Add("everyone_mask", (int)item.Permissions.EveryOne);
            resmap.Add("permissions", perm_info);
            resmap.Add("type", (int)item.InventoryType);
            resmap.Add("desc", item.Description);
            return resmap;
        }
    }
}
