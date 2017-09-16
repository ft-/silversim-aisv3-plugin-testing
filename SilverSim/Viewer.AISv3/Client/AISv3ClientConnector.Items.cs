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

using SilverSim.Http.Client;
using SilverSim.ServiceInterfaces.Inventory;
using SilverSim.Types;
using SilverSim.Types.Asset;
using SilverSim.Types.Inventory;
using SilverSim.Types.StructuredData.Llsd;
using System.Collections.Generic;
using System.IO;
using System.Web;

namespace SilverSim.Viewer.AISv3.Client
{
    public partial class AISv3ClientConnector : IInventoryItemServiceInterface
    {
        InventoryItem IInventoryItemServiceInterface.this[UUID key]
        {
            get
            {
                InventoryItem item;
                if(!Item.TryGetValue(key, out item))
                {
                    throw new InventoryItemNotFoundException(key);
                }
                return item;
            }
        }

        InventoryItem IInventoryItemServiceInterface.this[UUID principalID, UUID key]
        {
            get
            {
                InventoryItem item;
                if (!Item.TryGetValue(principalID, key, out item))
                {
                    throw new InventoryItemNotFoundException(key);
                }
                return item;
            }
        }

        List<InventoryItem> IInventoryItemServiceInterface.this[UUID principalID, List<UUID> itemids]
        {
            get
            {
                var result = new List<InventoryItem>();
                foreach (UUID id in itemids)
                {
                    InventoryItem item;
                    if (Item.TryGetValue(principalID, id, out item))
                    {
                        result.Add(item);
                    }
                }
                return result;
            }
        }

        void IInventoryItemServiceInterface.Add(InventoryItem item)
        {
            var m = new Map();
            var items = new AnArray();
            m.Add("items", items);
            var itemdata = new Map();
            items.Add(itemdata);
            itemdata.Add("asset_id", item.AssetID);
            itemdata.Add("inv_type", (int)item.InventoryType);
            itemdata.Add("name", item.Name);
            var saleinfo = new Map();
            itemdata.Add("sale_info", saleinfo);
            saleinfo.Add("sale_price", item.SaleInfo.Price);
            saleinfo.Add("sale_type", (int)item.SaleInfo.Type);
            itemdata.Add("flags", (int)item.Flags);
            itemdata.Add("desc", item.Description);
            itemdata.Add("type", (int)item.AssetType);
            var perminfo = new Map();
            itemdata.Add("permissions", perminfo);
            perminfo.Add("owner_mask", (int)item.Permissions.Current);
            perminfo.Add("base_mask", (int)item.Permissions.Base);
            perminfo.Add("next_owner_mask", (int)item.Permissions.NextOwner);
            perminfo.Add("group_mask", (int)item.Permissions.Group);
            perminfo.Add("everyone_mask", (int)item.Permissions.EveryOne);

            byte[] reqdata;
            using (var ms = new MemoryStream())
            {
                LlsdXml.Serialize(m, ms);
                reqdata = ms.ToArray();
            }

            IValue res;
            try
            {
                using (Stream sres = HttpClient.DoStreamRequest("POST", $"{m_CapabilityUri}category/{item.ParentFolderID}", null, "application/llsd+xml", reqdata.Length,
                    (Stream s) => s.Write(reqdata, 0, reqdata.Length), false, TimeoutMs))
                {
                    res = LlsdXml.Deserialize(sres);
                }
            }
            catch (HttpException e)
            {
                if (e.GetHttpCode() == 404)
                {
                    throw new InventoryFolderNotFoundException(item.ParentFolderID);
                }
                throw;
            }
            var resmap = res as Map;
            if(resmap == null)
            {
                throw new InvalidDataException();
            }

            var created_items = resmap["_created_items"] as AnArray;
            if(created_items == null)
            {
                throw new InvalidDataException();
            }
            item.SetNewID(created_items[0].AsUUID);
        }

        bool IInventoryItemServiceInterface.ContainsKey(UUID key)
        {
            InventoryItem item;
            return Item.TryGetValue(key, out item);
        }

        bool IInventoryItemServiceInterface.ContainsKey(UUID principalID, UUID key)
        {
            InventoryItem item;
            return Item.TryGetValue(principalID, key, out item);
        }

        void IInventoryItemServiceInterface.Delete(UUID principalID, UUID id)
        {
            HttpClient.DoRequest("DELETE", $"{m_CapabilityUri}item/{id}", null, string.Empty, string.Empty, false, TimeoutMs);
        }

        List<UUID> IInventoryItemServiceInterface.Delete(UUID principalID, List<UUID> ids)
        {
            var deleted = new List<UUID>();
            foreach (UUID id in ids)
            {
                try
                {
                    Item.Delete(principalID, id);
                    deleted.Add(id);
                }
                catch (InventoryItemNotFoundException)
                {
                    /* ignore */
                }
            }
            return deleted;
        }

        void IInventoryItemServiceInterface.Move(UUID principalID, UUID id, UUID newFolder)
        {
            var headers = new Dictionary<string, string>
            {
                ["Destination"] = $"{m_CapabilityUri}category/{newFolder}"
            };
            HttpClient.DoRequest("MOVE", $"{m_CapabilityUri}item/{id}", null, string.Empty, string.Empty, false, TimeoutMs, headers);
        }

        bool IInventoryItemServiceInterface.TryGetValue(UUID key, out InventoryItem item)
        {
            item = default(InventoryItem);
            IValue iv;
            try
            {
                using (Stream s = HttpClient.DoStreamGetRequest($"{m_CapabilityUri}item/{key}", null, TimeoutMs))
                {
                    iv = LlsdXml.Deserialize(s);
                }
            }
            catch (HttpException e)
            {
                if (e.GetHttpCode() == 404)
                {
                    return false;
                }
                throw;
            }
            var resmap = iv as Map;
            if (resmap == null)
            {
                throw new InvalidDataException("Wrong response received");
            }
            var links = resmap["_links"] as Map;
            if (links == null)
            {
                throw new InvalidDataException("Wrong response received");
            }
            var parent = links["parent"] as Map;
            if (parent == null)
            {
                throw new InvalidDataException("Wrong response received");
            }
            if (!parent.TryGetValue("href", out iv))
            {
                throw new InvalidDataException("Wrong response received");
            }

            string parenthref = iv.ToString();
            UUID parentid;
            if (!UUID.TryParse(parenthref.Substring(parenthref.Length - 36), out parentid))
            {
                InventoryFolder actFolder;
                if (!TryGetValue(parenthref, out actFolder))
                {
                    item = default(InventoryItem);
                    return false;
                }
                parentid = actFolder.ID;
            }

            item = new InventoryItem(resmap["item_id"].AsUUID)
            {
                InventoryType = (InventoryType)resmap["inv_type"].AsInt,
                Description = resmap["desc"].ToString(),
                Flags = (InventoryFlags)resmap["flags"].AsUInt,
                CreationDate = Date.UnixTimeToDateTime(resmap["created_at"].AsULong),
                AssetID = resmap["asset_id"].AsUUID,
                AssetType = (AssetType)resmap["type"].AsInt,
                Name = resmap["name"].ToString()
            };
            var saleinfo = resmap["sale_info"] as Map;
            item.SaleInfo.Price = saleinfo["sale_price"].AsInt;
            item.SaleInfo.Type = (InventoryItem.SaleInfoData.SaleType)saleinfo["sale_type"].AsInt;
            var perminfo = resmap["permissions"] as Map;
            item.Permissions.Base = (InventoryPermissionsMask)perminfo["base_mask"].AsUInt;
            item.Permissions.Group = (InventoryPermissionsMask)perminfo["group_mask"].AsUInt;
            item.LastOwner.ID = perminfo["last_owner_id"].AsUUID;
            item.Owner.ID = perminfo["owner_id"].AsUUID;
            item.Permissions.NextOwner = (InventoryPermissionsMask)perminfo["next_owner_mask"].AsUInt;
            item.Permissions.Current = (InventoryPermissionsMask)perminfo["owner_mask"].AsUInt;
            item.Group.ID = perminfo["group_id"].AsUUID;
            item.Permissions.EveryOne = (InventoryPermissionsMask)perminfo["everyone_mask"].AsUInt;

            return true;
        }

        bool IInventoryItemServiceInterface.TryGetValue(UUID principalID, UUID key, out InventoryItem item)
        {
            return Item.TryGetValue(key, out item);
        }

        void IInventoryItemServiceInterface.Update(InventoryItem item)
        {
            var m = new Map
            {
                { "name", item.Name },
                { "desc", item.Description },
                { "flags", (int)item.Flags },
                { "asset_id", item.AssetID },
                {
                    "sale_info", new Map
                    {
                        { "sale_price", item.SaleInfo.Price },
                        { "sale_type", (int)item.SaleInfo.Type }
                    }
                },
                {
                    "permisssions", new Map
                    {
                        { "owner_mask", (int)item.Permissions.Current },
                        { "everyone_mask", (int)item.Permissions.EveryOne },
                        { "next_owner_mask", (int)item.Permissions.NextOwner },
                        { "group_mask", (int)item.Permissions.Group }
                    }
                }
            };

            byte[] reqdata;
            using (var ms = new MemoryStream())
            {
                LlsdXml.Serialize(m, ms);
                reqdata = ms.ToArray();
            }

            HttpClient.DoRequest("PATCH", $"{m_CapabilityUri}item/{item.ID}", null, "application/llsd+xml", reqdata.Length,
                (Stream s) => s.Write(reqdata, 0, reqdata.Length), false, TimeoutMs);
        }
    }
}
