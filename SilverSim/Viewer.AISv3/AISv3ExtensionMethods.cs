﻿using SilverSim.Types;
using SilverSim.Types.Asset;
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

        public static List<InventoryItem>ItemsFromAisV3(this AnArray items_array, UUI agent, UUID parentFolderId)
        {
            List<InventoryItem> items = new List<InventoryItem>();
            foreach (Map item_map in items_array.GetValues<Map>())
            {
                InventoryItem item = new InventoryItem();
                item.Owner = agent;
                item.Creator = agent;
                item.ParentFolderID = parentFolderId;
                item.CreationDate = Date.Now;
                item.AssetID = item_map["asset_id"].AsUUID;
                item.InventoryType = (InventoryType)item_map["inv_type"].AsInt;
                item.Name = item_map["name"].ToString();
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
            List<InventoryItem> items = new List<InventoryItem>();
            foreach (Map item_map in items_array.GetValues<Map>())
            {
                InventoryItem item = new InventoryItem();
                item.Owner = agent;
                item.Creator = agent;
                item.ParentFolderID = parentFolderId;
                item.CreationDate = Date.Now;
                item.AssetID = item_map["linked_id"].AsUUID;
                item.AssetType = (AssetType)item_map["type"].AsInt;
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
            List<KeyValuePair<UUID, Map>> folders_stack = new List<KeyValuePair<UUID, Map>>();
            folders_stack.Add(new KeyValuePair<UUID, Map>(toParentFolderId, category));

            while(folders_stack.Count != 0)
            {
                KeyValuePair<UUID, Map> kvp = folders_stack[0];
                folders_stack.RemoveAt(0);
                InventoryFolder folder = new InventoryFolder();
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
