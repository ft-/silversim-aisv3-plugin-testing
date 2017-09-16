﻿// SilverSim is distributed under the terms of the
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
using System;
using System.Collections.Generic;
using System.IO;
using System.Web;

namespace SilverSim.Viewer.AISv3.Client
{
    public partial class AISv3ClientConnector : IInventoryFolderServiceInterface
    {
        InventoryFolder IInventoryFolderServiceInterface.this[UUID key] => throw new NotImplementedException();

        InventoryFolder IInventoryFolderServiceInterface.this[UUID principalID, AssetType type]
        {
            get
            {
                InventoryFolder folder;
                if(!Folder.TryGetValue(principalID, type, out folder))
                {
                    throw new InventoryFolderNotFoundException();
                }
                return folder;
            }
        }

        InventoryFolder IInventoryFolderServiceInterface.this[UUID principalID, UUID key]
        {
            get
            {
                InventoryFolder folder;
                if(!Folder.TryGetValue(principalID, key, out folder))
                {
                    throw new InventoryFolderNotFoundException(key);
                }
                return folder;
            }
        }

        IInventoryFolderContentServiceInterface IInventoryFolderServiceInterface.Content => this;

        void IInventoryFolderServiceInterface.Add(InventoryFolder folder)
        {
            var m = new Map();
            var categories = new AnArray();
            m.Add("categories", categories);
            var category = new Map();
            categories.Add(category);
            category.Add("name", folder.Name);
            category.Add("type_default", (int)folder.InventoryType);

            byte[] reqdata;
            using (var ms = new MemoryStream())
            {
                LlsdXml.Serialize(m, ms);
                reqdata = ms.ToArray();
            }

            IValue res;
            try
            {
                using (Stream sres = HttpClient.DoStreamRequest("POST", $"{m_CapabilityUri}category/{folder.ParentFolderID}", null, "application/llsd+xml", reqdata.Length,
                    (Stream s) => s.Write(reqdata, 0, reqdata.Length), false, TimeoutMs))
                {
                    res = LlsdXml.Deserialize(sres);
                }
            }
            catch(HttpException e)
            {
                if(e.GetHttpCode() == 404)
                {
                    throw new InventoryFolderNotFoundException(folder.ParentFolderID);
                }
                throw;
            }
            var resmap = res as Map;
            if (resmap == null)
            {
                throw new InvalidDataException();
            }

            var created_items = resmap["_created_categories"] as AnArray;
            if (created_items == null)
            {
                throw new InvalidDataException();
            }
            folder.ID = created_items[0].AsUUID;
        }

        bool IInventoryFolderServiceInterface.ContainsKey(UUID key)
        {
            InventoryFolder folder;
            return Folder.TryGetValue(key, out folder);
        }

        bool IInventoryFolderServiceInterface.ContainsKey(UUID principalID, UUID key)
        {
            InventoryFolder folder;
            return Folder.TryGetValue(principalID, key, out folder);
        }

        bool IInventoryFolderServiceInterface.ContainsKey(UUID principalID, AssetType type)
        {
            InventoryFolder folder;
            return Folder.TryGetValue(principalID, type, out folder);
        }

        void IInventoryFolderServiceInterface.Delete(UUID principalID, UUID folderID)
        {
            HttpClient.DoRequest("DELETE", $"{m_CapabilityUri}category/{folderID}", null, string.Empty, string.Empty, false, TimeoutMs);
        }

        List<UUID> IInventoryFolderServiceInterface.Delete(UUID principalID, List<UUID> folderIDs)
        {
            var deleted = new List<UUID>();
            foreach(UUID folderid in folderIDs)
            {
                try
                {
                    Folder.Delete(principalID, folderid);
                    deleted.Add(folderid);
                }
                catch(InventoryFolderNotFoundException)
                {
                    /* ignored */
                }
            }
            return deleted;
        }

        private static List<InventoryFolder> ExtractFolders(Map m)
        {
            var embmap = m["_embedded"] as Map;
            if (embmap == null)
            {
                return new List<InventoryFolder>();
            }
            var foldermap = embmap["categories"] as Map;
            if (foldermap == null)
            {
                return new List<InventoryFolder>();
            }

            var result = new List<InventoryFolder>();
            foreach (KeyValuePair<string, IValue> kvp in foldermap)
            {
                var folderdata = kvp.Value as Map;
                if (folderdata == null)
                {
                    continue;
                }
                var folder = new InventoryFolder(folderdata["category_id"].AsUUID)
                {
                    Name = folderdata["name"].ToString(),
                    InventoryType = (InventoryType)folderdata["type_default"].AsInt,
                    ParentFolderID = folderdata["parent_id"].AsUUID,
                    Version = folderdata["version"].AsInt,
                    Owner = new UUI(folderdata["agent_id"].AsUUID)
                };
                result.Add(folder);
            }
            return result;
        }

        private static List<InventoryItem> ExtractItems(Map m)
        {
            var embmap = m["_embedded"] as Map;
            if (embmap == null)
            {
                return new List<InventoryItem>();
            }
            var itemmap = embmap["items"] as Map;
            if (itemmap == null)
            {
                return new List<InventoryItem>();
            }

            var result = new List<InventoryItem>();
            foreach (KeyValuePair<string, IValue> kvp in itemmap)
            {
                var itemdata = kvp.Value as Map;
                if (itemdata == null)
                {
                    continue;
                }
                var saleinfo = itemdata["sale_info"] as Map;
                var perminfo = itemdata["permissions"] as Map;
                var item = new InventoryItem(itemdata["item_id"].AsUUID)
                {
                    AssetID = itemdata["asset_id"].AsUUID,
                    InventoryType = (InventoryType)itemdata["inv_type"].AsInt,
                    Name = itemdata["name"].ToString(),
                    SaleInfo = new InventoryItem.SaleInfoData
                    {
                        Price = saleinfo["sale_price"].AsInt,
                        Type = (InventoryItem.SaleInfoData.SaleType)saleinfo["sale_type"].AsInt
                    },
                    CreationDate = Date.UnixTimeToDateTime(itemdata["created_at"].AsULong),
                    ParentFolderID = itemdata["parent_id"].AsUUID,
                    Flags = (InventoryFlags)itemdata["flags"].AsInt,
                    Owner = new UUI(perminfo["owner_id"].AsUUID),
                    LastOwner = new UUI(perminfo["last_owner_id"].AsUUID),
                    Group = new UGI(perminfo["group_id"].AsUUID),
                    Permissions = new InventoryPermissionsData
                    {
                        Base = (InventoryPermissionsMask)perminfo["base_mask"].AsInt,
                        Group = (InventoryPermissionsMask)perminfo["group_mask"].AsInt,
                        NextOwner = (InventoryPermissionsMask)perminfo["next_owner_mask"].AsInt,
                        Current = (InventoryPermissionsMask)perminfo["owner_mask"].AsInt,
                        EveryOne = (InventoryPermissionsMask)perminfo["everyone_mask"].AsInt
                    },
                    AssetType = (AssetType)itemdata["type"].AsInt,
                    Description = itemdata["desc"].ToString()
                };
                result.Add(item);
            }
            return result;
        }

        List<InventoryFolder> IInventoryFolderServiceInterface.GetFolders(UUID principalID, UUID key)
        {
            IValue iv;
            try
            {
                using (Stream s = HttpClient.DoStreamGetRequest($"{m_CapabilityUri}category/{key}/categories?0,1", null, TimeoutMs))
                {
                    iv = LlsdXml.Deserialize(s);
                }
            }
            catch (HttpException e)
            {
                if (e.GetHttpCode() == 404)
                {
                    throw new InventoryFolderNotFoundException(key);
                }
                throw;
            }

            var resmap = iv as Map;
            if (resmap == null)
            {
                throw new InvalidDataException();
            }
            return ExtractFolders(resmap);
        }

        List<InventoryItem> IInventoryFolderServiceInterface.GetItems(UUID principalID, UUID key)
        {
            IValue iv;
            try
            {
                using (Stream s = HttpClient.DoStreamGetRequest($"{m_CapabilityUri}category/{key}/items", null, TimeoutMs))
                {
                    iv = LlsdXml.Deserialize(s);
                }
            }
            catch(HttpException e)
            {
                if(e.GetHttpCode() == 404)
                {
                    throw new InventoryFolderNotFoundException(key);
                }
                throw;
            }

            var resmap = iv as Map;
            if(resmap == null)
            {
                throw new InvalidDataException();
            }
            return ExtractItems(resmap);
        }

        void IInventoryFolderServiceInterface.IncrementVersion(UUID principalID, UUID folderID)
        {
            /* intentionally left empty */
        }

        void IInventoryFolderServiceInterface.Move(UUID principalID, UUID folderID, UUID toFolderID)
        {
            var headers = new Dictionary<string, string>
            {
                ["Destination"] = $"{m_CapabilityUri}category/{toFolderID}"
            };
            HttpClient.DoRequest("MOVE", $"{m_CapabilityUri}category/{folderID}", null, string.Empty, string.Empty, false, TimeoutMs, headers);
        }

        void IInventoryFolderServiceInterface.Purge(UUID folderID)
        {
            HttpClient.DoRequest("DELETE", $"{m_CapabilityUri}category/{folderID}/children", null, string.Empty, string.Empty, false, TimeoutMs);
        }

        void IInventoryFolderServiceInterface.Purge(UUID principalID, UUID folderID)
        {
            Folder.Purge(principalID, folderID);
        }

        bool IInventoryFolderServiceInterface.TryGetValue(UUID key, out InventoryFolder folder)
        {
            return TryGetValue($"{m_CapabilityUri}category/{key}", out folder);
        }

        private bool TryGetValue(string url, out InventoryFolder folder)
        {
            folder = default(InventoryFolder);
            IValue iv;
            try
            {
                using (Stream s = HttpClient.DoStreamGetRequest(url + "?0,0", null, TimeoutMs))
                {
                    iv = LlsdXml.Deserialize(s);
                }
            }
            catch(HttpException e)
            {
                if(e.GetHttpCode() == 404)
                {
                    return false;
                }
                throw;
            }
            var m = iv as Map;
            if(m == null)
            {
                throw new InvalidDataException();
            }
            folder = new InventoryFolder(m["category_id"].AsUUID);
            folder.Name = m["name"].ToString();
            folder.InventoryType = (InventoryType)m["type_default"].AsInt;
            folder.ParentFolderID = m["parent_id"].AsUUID;
            folder.Version = m["version"].AsInt;
            folder.Owner.ID = m["agent_id"].AsUUID;
            return true;
        }

        bool IInventoryFolderServiceInterface.TryGetValue(UUID principalID, UUID key, out InventoryFolder folder)
        {
            return Folder.TryGetValue(key, out folder);
        }

        bool IInventoryFolderServiceInterface.TryGetValue(UUID principalID, AssetType type, out InventoryFolder folder)
        {
            folder = default(InventoryFolder);
            string folderUrl;
            switch (type)
            {
                case AssetType.Animation:
                    folderUrl = m_CapabilityUri + "category/animatn";
                    break;

                case AssetType.Bodypart:
                    folderUrl = m_CapabilityUri + "category/bodypart";
                    break;

                case AssetType.Clothing:
                    folderUrl = m_CapabilityUri + "category/clothing";
                    break;

                case AssetType.CurrentOutfitFolder:
                    folderUrl = m_CapabilityUri + "category/current";
                    break;

                case AssetType.FavoriteFolder:
                    folderUrl = m_CapabilityUri + "category/favorite";
                    break;

                case AssetType.Gesture:
                    folderUrl = m_CapabilityUri + "category/gesture";
                    break;

                case AssetType.Inbox:
                    folderUrl = m_CapabilityUri + "category/inbox";
                    break;

                case AssetType.Landmark:
                    folderUrl = m_CapabilityUri + "category/landmark";
                    break;

                case AssetType.LSLText:
                    folderUrl = m_CapabilityUri + "category/lsltext";
                    break;

                case AssetType.LostAndFoundFolder:
                    folderUrl = m_CapabilityUri + "category/lstndfnd";
                    break;

                case AssetType.MyOutfitsFolder:
                    folderUrl = m_CapabilityUri + "category/my_otfts";
                    break;

                case AssetType.Notecard:
                    folderUrl = m_CapabilityUri + "category/notecard";
                    break;

                case AssetType.Object:
                    folderUrl = m_CapabilityUri + "category/object";
                    break;

                case AssetType.Outbox:
                    folderUrl = m_CapabilityUri + "category/outbox";
                    break;

                case AssetType.RootFolder:
                    folderUrl = m_CapabilityUri + "category/root";
                    break;

                case AssetType.SnapshotFolder:
                    folderUrl = m_CapabilityUri + "category/snapshot";
                    break;

                case AssetType.Sound:
                    folderUrl = m_CapabilityUri + "category/sound";
                    break;

                case AssetType.Texture:
                    folderUrl = m_CapabilityUri + "category/texture";
                    break;

                case AssetType.TrashFolder:
                    folderUrl = m_CapabilityUri + "category/trash";
                    break;

                default:
                    return false;
            }
            return TryGetValue(folderUrl, out folder);
        }

        void IInventoryFolderServiceInterface.Update(InventoryFolder folder)
        {
            var m = new Map
            {
                { "name", folder.Name }
            };

            byte[] reqdata;
            using (var ms = new MemoryStream())
            {
                LlsdXml.Serialize(m, ms);
                reqdata = ms.ToArray();
            }

            HttpClient.DoRequest("PATCH", $"{m_CapabilityUri}category/{folder.ID}", null, "application/llsd+xml", reqdata.Length,
                (Stream s) => s.Write(reqdata, 0, reqdata.Length), false, TimeoutMs);
        }
    }
}