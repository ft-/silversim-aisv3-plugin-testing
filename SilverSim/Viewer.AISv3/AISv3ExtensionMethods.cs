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
using System.Collections.Generic;
using System.Linq;

namespace SilverSim.Viewer.AISv3
{
    public static class AISv3ExtensionMethods
    {
        public static Map ToAisV3(this InventoryFolder folder) => new Map
        {
            { "name", folder.Name },
            { "type_default", (int)folder.InventoryType },
            { "parent_id", folder.ParentFolderID },
            { "version", folder.Version },
            { "agent_id", folder.Owner.ID },
            { "category_id", folder.ID }
        };

        public static List<InventoryItem>ItemsFromAisV3(this AnArray items_array, UUI agent, UUID parentFolderId)
        {
            var items = new List<InventoryItem>();
            foreach (Map item_map in items_array.GetValues<Map>())
            {
                var item = new InventoryItem()
                {
                    Owner = agent,
                    Creator = agent,
                    ParentFolderID = parentFolderId,
                    CreationDate = Date.Now,
                    AssetID = item_map["asset_id"].AsUUID,
                    InventoryType = (InventoryType)item_map["inv_type"].AsInt,
                    Name = item_map["name"].ToString()
                };
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
                    if (perm_info.TryGetValue("owner_mask", out intval))
                    {
                        item.Permissions.NextOwner = (InventoryPermissionsMask)intval.AsInt;
                    }
                    if (perm_info.TryGetValue("base_mask", out intval))
                    {
                        item.Permissions.Base = (InventoryPermissionsMask)intval.AsInt;
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
                }
                items.Add(item);
            }
            return items;
        }

        public static List<InventoryItem> LinksFromAisV3(this AnArray items_array, UUI agent, UUID parentFolderId)
        {
            var items = new List<InventoryItem>();
            foreach (Map item_map in items_array.GetValues<Map>())
            {
                var item = new InventoryItem()
                {
                    Owner = agent,
                    Creator = agent,
                    ParentFolderID = parentFolderId,
                    CreationDate = Date.Now,
                    AssetID = item_map["linked_id"].AsUUID,
                    AssetType = (AssetType)item_map["type"].AsInt
                };
                IValue iv;
                if(item_map.TryGetValue("name", out iv))
                {
                    item.Name = iv.ToString();
                }
                if(item_map.TryGetValue("desc", out iv))
                {
                    item.Description = iv.ToString();
                }
                items.Add(item);
            }
            return items;
        }

        public static Map ToAisV3(this InventoryItem item)
        {
            var resmap = new Map
            {
                { "asset_id", item.AssetID },
                { "inv_type", (int)item.InventoryType },
                { "name", item.Name }
            };
            var sale_info = new Map
            {
                { "sale_price", item.SaleInfo.Price },
                { "sale_type", (int)item.SaleInfo.Type }
            };
            resmap.Add("sale_info", sale_info);
            resmap.Add("created_at", item.CreationDate.DateTimeToUnixTime());
            resmap.Add("parent_id", item.ParentFolderID);
            resmap.Add("flags", (int)item.Flags);
            resmap.Add("agent_id", item.Owner.ID);
            resmap.Add("item_id", item.ID);
            var perm_info = new Map
            {
                { "base_mask", (int)item.Permissions.Base },
                { "group_mask", (int)item.Permissions.Group },
                { "last_owner_id", item.LastOwner.ID },
                { "owner_id", item.Owner.ID },
                { "creator_id", item.Creator.ID },
                { "next_owner_mask", (int)item.Permissions.NextOwner },
                { "owner_mask", (int)item.Permissions.Current },
                { "group_id", item.Group.ID },
                { "everyone_mask", (int)item.Permissions.EveryOne }
            };
            resmap.Add("permissions", perm_info);
            resmap.Add("type", (int)item.InventoryType);
            resmap.Add("desc", item.Description);
            return resmap;
        }

        public static void CategoriesFromAisV3(this AnArray categories_map, UUI agent, UUID toParentFolderId, List<InventoryFolder> folders, List<InventoryItem> items, List<InventoryItem> itemlinks)
        {
            foreach(Map m in categories_map.GetValues<Map>())
            {
                m.CategoryFromAisV3(agent, toParentFolderId, folders, items, itemlinks);
            }
        }

        public static void CategoryFromAisV3(this Map category, UUI agent, UUID toParentFolderId, List<InventoryFolder> folders, List<InventoryItem> items, List<InventoryItem> itemlinks)
        {
            /* we use a stack based algo here instead of recursive (ParentFolderID, Category_Map) */
            var folders_stack = new List<KeyValuePair<UUID, Map>>();
            folders_stack.Add(new KeyValuePair<UUID, Map>(toParentFolderId, category));

            while(folders_stack.Count != 0)
            {
                KeyValuePair<UUID, Map> kvp = folders_stack[0];
                folders_stack.RemoveAt(0);
                var folder = new InventoryFolder();
                folder.ParentFolderID = kvp.Key;
                folder.Owner = agent;
                folder.InventoryType = InventoryType.Unknown;
                Map category_map = kvp.Value;
                folder.Name = category_map["name"].ToString();
                Integer intval;
                if (category_map.TryGetValue("type_default", out intval))
                {
                    folder.InventoryType = (InventoryType)intval.AsInt;
                }
                folder.Version = 1;
                Map emb_map;
                if (category_map.TryGetValue("_embedded", out emb_map))
                {
                    AnArray array;
                    if (category_map.TryGetValue("items", out array))
                    {
                        items.AddRange(array.ItemsFromAisV3(agent, folder.ID));
                    }

                    if (category_map.TryGetValue("links", out array))
                    {
                        List<InventoryItem> links = array.LinksFromAisV3(agent, folder.ID);
                        items.AddRange(from link in links where link.AssetType == AssetType.LinkFolder select link);
                        itemlinks.AddRange(from link in links where link.AssetType == AssetType.Link select link);
                    }

                    if(category_map.TryGetValue("categories", out array))
                    {
                        foreach(Map m in array.GetValues<Map>())
                        {
                            folders_stack.Add(new KeyValuePair<UUID, Map>(folder.ID, m));
                        }
                    }
                }
            }
        }
    }
}
