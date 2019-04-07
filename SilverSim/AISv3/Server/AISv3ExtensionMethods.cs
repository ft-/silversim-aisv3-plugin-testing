// SilverSim is distributed under the terms of the
// GNU Affero General Public License v3 with
// the following clarification and special exception.

// Linking this library statically or dynamically with other modules is
// making a combined work based on this library. Thus, the terms and
// conditions of the GNU Affero General Public License cover the whole
// combination.

// As a special exception, the copyright holders of this library give you
// permission to link this library with independent modules to produce an
// executable, regardless of the license terms of these independent
// modules, and to copy and distribute the resulting executable under
// terms of your choice, provided that you also meet, for each linked
// independent module, the terms and conditions of the license of that
// module. An independent module is a module which is not derived from
// or based on this library. If you modify this library, you may extend
// this exception to your version of the library, but you are not
// obligated to do so. If you do not wish to do so, delete this
// exception statement from your version.

using SilverSim.Types;
using SilverSim.Types.Asset;
using SilverSim.Types.Inventory;

namespace SilverSim.AISv3.Server
{
    public static class AISv3ExtensionMethods
    {
        public static Map ToAisV3Href(this string uri) => new Map { { "href", uri } };

        public static Map ToAisV3(this InventoryFolder folder, string fullprefixuri) => new Map
        {
            { "name", folder.Name },
            { "type_default", (int)folder.DefaultType },
            { "parent_id", folder.ParentFolderID },
            { "version", folder.Version },
            { "agent_id", folder.Owner.ID },
            { "category_id", folder.ID },
            {
                "_links", new Map
                {
                    { "self", ToAisV3Href(fullprefixuri + "/category/" + folder.ID.ToString()) },
                    { "parent", ToAisV3Href(fullprefixuri + "/category/" + folder.ParentFolderID.ToString()) },
                    { "links", ToAisV3Href(fullprefixuri + "/category/" + folder.ID.ToString() + "/links") },
                    { "items", ToAisV3Href(fullprefixuri + "/category/" + folder.ID.ToString() + "/items") },
                    { "children", ToAisV3Href(fullprefixuri + "/category/" + folder.ID.ToString() + "/children") }
                }
            }
        };

        public static Map ToAisV3(this InventoryItem item, string fullprefixuri)
        {
            var resmap = new Map
            {
                { "inv_type", (int)item.InventoryType },
                { "name", item.Name }
            };
            if(item.AssetType == AssetType.Link || item.AssetType == AssetType.LinkFolder)
            {
                resmap.Add("linked_id", item.AssetID);
            }
            else
            {
                resmap.Add("asset_id", item.AssetID);
            }
            resmap.Add("created_at", item.CreationDate.DateTimeToUnixTime().ToString());
            resmap.Add("parent_id", item.ParentFolderID);
            if (item.AssetType != AssetType.Link && item.AssetType != AssetType.LinkFolder)
            {
                resmap.Add("flags", (int)item.Flags);
            }
            resmap.Add("agent_id", item.Owner.ID);
            resmap.Add("item_id", item.ID);
            resmap.Add("type", (int)item.AssetType);
            resmap.Add("desc", item.Description);
            if (item.AssetType != AssetType.Link && item.AssetType != AssetType.LinkFolder)
            {
                var saleinfo = new Map
                {
                    { "sale_price", item.SaleInfo.Price },
                    { "sale_type", (int)item.SaleInfo.Type }
                };
                resmap.Add("sale_info", saleinfo);
                var perminfo = new Map
                {
                    { "base_mask", (int)item.Permissions.Base },
                    { "group_mask", (int)item.Permissions.Group },
                    { "last_owner_id", item.LastOwner.ID },
                    { "owner_id", item.Owner.ID },
                    { "owner_mask", (int)item.Permissions.Current },
                    { "creator_id", item.Creator.ID },
                    { "next_owner_mask", (int)item.Permissions.NextOwner },
                    { "group_id", item.Group.ID },
                    { "everyone_mask", (int)item.Permissions.EveryOne }
                };
                resmap.Add("permissions", perminfo);
            }
            var linkref = new Map
            {
                { "self", ToAisV3Href(fullprefixuri + "/item/" + item.ID.ToString()) },
                { "parent", ToAisV3Href(fullprefixuri + "/category/" + item.ParentFolderID.ToString()) }
            };
            if(item.AssetType == AssetType.Link)
            {
                linkref.Add("item", ToAisV3Href(fullprefixuri + "/item/" + item.AssetID.ToString()));
            }
            else if(item.AssetType == AssetType.LinkFolder)
            {
                linkref.Add("category", ToAisV3Href(fullprefixuri + "/category/" + item.AssetID.ToString()));
            }
            resmap.Add("_links", linkref);
            return resmap;
        }

        public static InventoryItem ItemFromAisV3(this Map item_map, UGUI agent, UUID toParentFolderId, string fullprefixuri)
        {
            var item = new InventoryItem(UUID.Random)
            {
                Owner = agent,
                Creator = agent,
                ParentFolderID = toParentFolderId,
                CreationDate = Date.Now,
                InventoryType = (InventoryType)item_map["inv_type"].AsInt,
                Name = item_map["name"].ToString()
            };
            UUID id;
            if(item_map.TryGetValue("asset_id", out id))
            {
                item.AssetID = id;
            }
            else if(item_map.TryGetValue("linked_id", out id))
            {
                item.AssetID = id;
            }
            Integer intval;
            Map sale_info;
            if (item_map.TryGetValue("sale_info", out sale_info))
            {
                if (sale_info.TryGetValue("sale_price", out intval))
                {
                    item.SaleInfo.Price = intval;
                }
                if (sale_info.TryGetValue("sale_type", out intval))
                {
                    item.SaleInfo.Type = (InventoryItem.SaleInfoData.SaleType)intval.AsInt;
                }
            }
            if (item_map.TryGetValue("flags", out intval))
            {
                item.Flags = (InventoryFlags)intval.AsInt;
            }
            IValue desc;
            if (item_map.TryGetValue("desc", out desc))
            {
                item.Description = desc.ToString();
            }
            item.AssetType = (AssetType)item_map["type"].AsInt;
            Map perm_info;
            if (item_map.TryGetValue("permissions", out perm_info))
            {
                if (perm_info.TryGetValue("base_mask", out intval))
                {
                    item.Permissions.Base = (InventoryPermissionsMask)intval.AsInt;
                }
                if (perm_info.TryGetValue("owner_mask", out intval))
                {
                    item.Permissions.Current = (InventoryPermissionsMask)intval.AsInt;
                }
                if (perm_info.TryGetValue("next_owner_mask", out intval))
                {
                    item.Permissions.NextOwner = (InventoryPermissionsMask)intval.AsInt;
                }
                if (perm_info.TryGetValue("group_mask", out intval))
                {
                    item.Permissions.Group = (InventoryPermissionsMask)intval.AsInt;
                }
                if (perm_info.TryGetValue("everyone_mask", out intval))
                {
                    item.Permissions.EveryOne = (InventoryPermissionsMask)intval.AsInt;
                }
                UUID uuid;
                if(perm_info.TryGetValue("last_owner_id", out uuid))
                {
                    item.LastOwner.ID = uuid;
                }
                if (perm_info.TryGetValue("creator_id", out uuid))
                {
                    item.Creator.ID = uuid;
                }
                if(perm_info.TryGetValue("group_id", out uuid))
                {
                    item.Group.ID = uuid;
                }
            }

            var linkref = new Map
            {
                { "self", ToAisV3Href(fullprefixuri + "/item/" + item.ID.ToString()) },
                { "parent", ToAisV3Href(fullprefixuri + "/category/" + item.ParentFolderID.ToString()) }
            };
            if(item.AssetType == AssetType.Link)
            {
                linkref.Add("item", ToAisV3Href(fullprefixuri + "/item/" + item.ID.ToString()));
            }
            else if(item.AssetType == AssetType.LinkFolder)
            {
                linkref.Add("category", ToAisV3Href(fullprefixuri + "/category/" + item.ID.ToString()));
            }
            item_map.Add("_links", linkref);

            return item;
        }

        public static InventoryFolder CategoryFromAisV3(this Map category_map, UGUI agent, UUID toParentFolderId, string fullprefixuri)
        {
            var folder = new InventoryFolder(UUID.Random);
            Integer intval;
            folder.Name = category_map["name"].ToString();
            if (category_map.TryGetValue("type_default", out intval))
            {
                folder.DefaultType = (AssetType)intval.AsInt;
            }
            folder.Owner = agent;
            folder.ParentFolderID = toParentFolderId;
            folder.Version = 1;
            var linkref = new Map
            {
                { "self", ToAisV3Href(fullprefixuri + "/category/" + folder.ID.ToString()) },
                { "parent", ToAisV3Href(fullprefixuri + "/category/" + folder.ParentFolderID.ToString()) },
                { "links", ToAisV3Href(fullprefixuri + "/category/" + folder.ID.ToString() + "/links") },
                { "items", ToAisV3Href(fullprefixuri + "/category/" + folder.ID.ToString() + "/items") },
                { "children", ToAisV3Href(fullprefixuri + "/category/" + folder.ID.ToString() + "/children") }
            };
            category_map.Add("_links", linkref);
            category_map.Add("category_id", folder.ID);
            category_map.Add("parent_id", folder.ParentFolderID);
            category_map.Add("agent_id", folder.Owner.ID);
            return folder;
        }
    }
}
