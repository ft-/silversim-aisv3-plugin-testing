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
using System.Net;
using System.Web;

/* initially disabling Obsolete here */
#pragma warning disable CS0618

namespace SilverSim.AISv3.Client
{
    public partial class AISv3ClientConnector : IInventoryItemServiceInterface
    {
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
            perminfo.Add("owner_id", item.Owner.ID);
            perminfo.Add("last_owner_id", item.LastOwner.ID);
            perminfo.Add("creator_id", item.Creator.ID);
            itemdata.Add("agent_id", item.Owner.ID);
            perminfo.Add("group_id", item.Group.ID);
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
                using (Stream sres = new HttpClient.Post(
                    $"{m_CapabilityUri}category/{item.ParentFolderID}",
                    "application/llsd+xml",
                    reqdata.Length,
                    (Stream s) => s.Write(reqdata, 0, reqdata.Length))
                {
                    TimeoutMs = TimeoutMs
                }.ExecuteStreamRequest())
                {
                    res = LlsdXml.Deserialize(sres);
                }
            }
            catch (HttpException e)
            {
                if (e.GetHttpCode() == 404)
                {
                    throw new InvalidParentFolderIdException();
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
            if(new HttpClient.Delete($"{m_CapabilityUri}item/{id}")
            {
                TimeoutMs = TimeoutMs
            }.ExecuteStatusRequest() == System.Net.HttpStatusCode.NotFound)
            {
                throw new InventoryItemNotFoundException(id);
            }
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
            switch(new HttpClient.Move($"{m_CapabilityUri}item/{id}")
            {
                TimeoutMs = TimeoutMs,
                Headers = headers,
                DisableExceptions = HttpClient.Request.DisableExceptionFlags.DisableGone
            }.ExecuteStatusRequest())
            { 
                case HttpStatusCode.NotFound: throw new InvalidParentFolderIdException();
                case HttpStatusCode.Gone: throw new InventoryItemNotFoundException(id);
            }
        }

        UUID IInventoryItemServiceInterface.Copy(UUID principalID, UUID id, UUID newFolder)
        {
            var headers = new Dictionary<string, string>
            {
                ["Destination"] = $"{m_CapabilityUri}category/{newFolder}"
            };
            Map res;
            HttpStatusCode statuscode;
            using (Stream s = new HttpClient.Copy($"{m_CapabilityUri}item/{id}")
            {
                TimeoutMs = TimeoutMs,
                Headers = headers,
                DisableExceptions = HttpClient.Request.DisableExceptionFlags.DisableForbidden | HttpClient.Request.DisableExceptionFlags.DisableGone
            }.ExecuteStreamRequest(out statuscode))
            {
                switch (statuscode)
                {
                    case HttpStatusCode.OK:
                        break;
                    case HttpStatusCode.Forbidden:
                        throw new InventoryItemNotCopiableException(id);
                    case HttpStatusCode.Gone:
                        throw new InventoryItemNotFoundException(id);
                    default:
                        throw new InvalidParentFolderIdException();
                }
                res = (Map)LlsdXml.Deserialize(s);
            }

            AnArray created_items;

            if (!res.TryGetValue("_created_items", out created_items) || created_items.Count != 1)
            {
                throw new InventoryItemNotStoredException();
            }
            return created_items[0].AsUUID;
        }

        bool IInventoryItemServiceInterface.TryGetValue(UUID key, out InventoryItem item)
        {
            item = default(InventoryItem);
            IValue iv;
            HttpStatusCode statuscode;
            using (Stream s = new HttpClient.Get($"{m_CapabilityUri}item/{key}")
            {
                TimeoutMs = TimeoutMs
            }.ExecuteStreamRequest(out statuscode))
            {
                if(statuscode != HttpStatusCode.OK)
                {
                    return false;
                }
                iv = LlsdXml.Deserialize(s);
            }
            var resmap = iv as Map;
            if (resmap == null)
            {
                throw new InvalidDataException("Wrong response received");
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
            item.Creator.ID = perminfo["creator_id"].AsUUID;
            item.ParentFolderID = resmap["parent_id"].AsUUID;
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
            Map m;
            if (item.AssetType == AssetType.Link || item.AssetType == AssetType.LinkFolder)
            {
                m = new Map
                {
                    { "name", item.Name },
                    { "desc", item.Description },
                    { "linked_id", item.AssetID }
                };
            }
            else
            {
                m = new Map
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
                        "permissions", new Map
                        {
                            { "owner_mask", (int)item.Permissions.Current },
                            { "everyone_mask", (int)item.Permissions.EveryOne },
                            { "next_owner_mask", (int)item.Permissions.NextOwner },
                            { "group_mask", (int)item.Permissions.Group }
                        }
                    }
                };
            }

            byte[] reqdata;
            using (var ms = new MemoryStream())
            {
                LlsdXml.Serialize(m, ms);
                reqdata = ms.ToArray();
            }

            if(new HttpClient.Patch(
                $"{m_CapabilityUri}item/{item.ID}",
                "application/llsd+xml",
                reqdata.Length,
                (Stream s) => s.Write(reqdata, 0, reqdata.Length))
            {
                TimeoutMs = TimeoutMs
            }.ExecuteStatusRequest() == HttpStatusCode.NotFound)
            {
                throw new InventoryItemNotFoundException(item.ID);
            }
        }
    }
}
