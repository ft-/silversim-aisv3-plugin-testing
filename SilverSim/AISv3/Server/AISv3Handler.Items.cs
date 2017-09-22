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

using SilverSim.Main.Common.HttpServer;
using SilverSim.ServiceInterfaces.Inventory;
using SilverSim.Types;
using SilverSim.Types.Asset;
using SilverSim.Types.Inventory;
using SilverSim.Types.StructuredData.Llsd;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace SilverSim.AISv3.Server
{
    public partial class AISv3Handler
    {
        #region Item Handling
        private static void ItemHandler(Request req, string[] elements)
        {
            switch (req.HttpRequest.Method)
            {
                case "GET":
                    ItemHandler_Get(req, elements);
                    break;

                case "PATCH":
                    ItemHandler_Patch(req, elements);
                    break;

                case "COPY":
                    ItemHandler_Copy(req, elements);
                    break;

                case "MOVE":
                    ItemHandler_Move(req, elements);
                    break;

                case "DELETE":
                    ItemHandler_Delete(req, elements);
                    break;

                default:
                    ErrorResponse(req, HttpStatusCode.MethodNotAllowed, AisErrorCode.MethodNotAllowed, "Method not allowed");
                    break;
            }
        }

        private static void ItemHandler_Get(Request req, string[] elements)
        {
            UUID itemid;

            if (!UUID.TryParse(elements[1], out itemid))
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }

            InventoryItem item;
            if (!req.InventoryService.Item.TryGetValue(req.Agent.ID, itemid, out item))
            {
                ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Not Found");
                return;
            }

            var folderCache = new Dictionary<UUID, InventoryFolder>();
            Map resmap;
            if (item.AssetType == AssetType.Link)
            {
                resmap = item.ToAisV3(req.FullPrefixUrl);
                resmap.Add("_base_uri", req.FullPrefixUrl + "/item/" + itemid.ToString());
                InventoryItem linkeditem;
                if (req.InventoryService.Item.TryGetValue(req.Agent.ID, item.AssetID, out linkeditem))
                {
                    var embmap = new Map();
                    resmap.Add("_embedded", embmap);
                    embmap.Add("item", linkeditem.ToAisV3(req.FullPrefixUrl));
                }
            }
            else if (item.AssetType == AssetType.LinkFolder)
            {
                resmap = item.ToAisV3(req.FullPrefixUrl);
                resmap.Add("_base_uri", req.FullPrefixUrl + "/item/" + itemid.ToString());
                InventoryFolder linkedfolder;
                if (TryGetFolder(req, item.AssetID, out linkedfolder, folderCache))
                {
                    var embmap = new Map();
                    resmap.Add("_embedded", embmap);
                    var foldermap = linkedfolder.ToAisV3(req.FullPrefixUrl);
                    embmap.Add("category", foldermap);
                }
            }
            else
            {
                resmap = item.ToAisV3(req.FullPrefixUrl);
                resmap.Add("_base_uri", req.FullPrefixUrl + "/item/" + itemid.ToString());
            }
            SuccessResponse(req, resmap);
        }

        private static void ItemHandler_Patch(Request req, string[] elements)
        {
            if (req.HttpRequest.ContentType != "application/llsd+xml")
            {
                ErrorResponse(req, HttpStatusCode.UnsupportedMediaType, AisErrorCode.UnsupportedMedia, "Unsupported media type");
                return;
            }

            UUID itemid;

            if (!UUID.TryParse(elements[1], out itemid))
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }

            Map reqmap;
            try
            {
                using (Stream s = req.HttpRequest.Body)
                {
                    reqmap = (Map)LlsdXml.Deserialize(s);
                }
            }
            catch
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }

            InventoryItem item;
            if(!req.InventoryService.Item.TryGetValue(req.Agent.ID, itemid, out item))
            {
                ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Not Found");
                return;
            }

            IValue iv;
            if (item.AssetType == AssetType.Link || item.AssetType == AssetType.LinkFolder)
            {
                if(reqmap.TryGetValue("linked_id", out iv))
                {
                    item.AssetID = iv.AsUUID;
                }
                if(reqmap.TryGetValue("name", out iv))
                {
                    item.Name = iv.ToString();
                }
                if (reqmap.TryGetValue("desc", out iv))
                {
                    item.Description = iv.ToString();
                }
            }
            else
            {
                if(reqmap.TryGetValue("asset_id", out iv))
                {
                    item.AssetID = iv.AsUUID;
                }
                if (reqmap.TryGetValue("name", out iv))
                {
                    item.Name = iv.ToString();
                }
                Map sale_info;
                if (reqmap.TryGetValue("sale_info", out sale_info))
                {
                    if (sale_info.TryGetValue("sale_price", out iv))
                    {
                        item.SaleInfo.Price = iv.AsInt;
                    }
                    if(sale_info.TryGetValue("sale_type", out iv))
                    {
                        item.SaleInfo.Type = (InventoryItem.SaleInfoData.SaleType)iv.AsInt;
                    }
                }
                if(reqmap.TryGetValue("flags", out iv))
                {
                    item.Flags = (InventoryFlags)iv.AsInt;
                }
                if(reqmap.TryGetValue("desc", out iv))
                {
                    item.Description = iv.ToString();
                }
                Map perminfo;
                if(reqmap.TryGetValue("permissions", out perminfo))
                {
                    if (perminfo.TryGetValue("owner_mask", out iv))
                    {
                        item.Permissions.Current = (InventoryPermissionsMask)iv.AsInt & item.Permissions.Base;
                    }
                    if(perminfo.TryGetValue("everyone_mask", out iv))
                    {
                        item.Permissions.EveryOne = (InventoryPermissionsMask)iv.AsInt & item.Permissions.Base;
                    }
                    if (perminfo.TryGetValue("next_owner_mask", out iv))
                    {
                        item.Permissions.NextOwner = (InventoryPermissionsMask)iv.AsInt & item.Permissions.Base;
                    }
                    if (perminfo.TryGetValue("group_mask", out iv))
                    {
                        item.Permissions.Group = (InventoryPermissionsMask)iv.AsInt & item.Permissions.Base;
                    }
                }
            }

            try
            {
                req.InventoryService.Item.Update(item);
            }
            catch
            {
                ErrorResponse(req, HttpStatusCode.Forbidden, AisErrorCode.QueryFailed, "Forbidden");
            }
            Map resdata = item.ToAisV3(req.FullPrefixUrl);
            resdata.Add("_base_uri", req.FullPrefixUrl + "/item/" + item.ID.ToString());
            resdata.Add("_updated_items", new AnArray { item.ID });
            InventoryFolder parentFolder;
            if (req.InventoryService.Folder.TryGetValue(item.ParentFolderID, out parentFolder))
            {
                resdata.Add("_updated_category_versions", new Map { { parentFolder.ID.ToString(), parentFolder.Version } });
            }
            InventoryItem linkeditem;
            InventoryFolder linkedfolder;
            if (item.AssetType == AssetType.Link && req.InventoryService.Item.TryGetValue(req.Agent.ID, item.AssetID, out linkeditem))
            {
                Map itemdata = linkeditem.ToAisV3(req.FullPrefixUrl);
                var embedded = new Map
                {
                    ["item"] = itemdata
                };
                resdata.Add("_embedded", embedded);
                resdata.Add("_broken", false);
            }
            else if(item.AssetType == AssetType.LinkFolder && req.InventoryService.Folder.TryGetValue(item.AssetID, out linkedfolder))
            {
                Map itemdata = linkedfolder.ToAisV3(req.FullPrefixUrl);
                var embedded = new Map
                {
                    ["category"] = itemdata
                };
                resdata.Add("_embedded", embedded);
                resdata.Add("_broken", false);
            }
            else
            {
                resdata.Add("_broken", true);
            }
            SuccessResponse(req, resdata);
        }

        private static void ItemHandler_Copy(Request req, string[] elements)
        {
            UUID itemid;

            if (!UUID.TryParse(elements[1], out itemid))
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }

            string destinationurl = req.HttpRequest["Destination"];
            if (!destinationurl.StartsWith(req.FullPrefixUrl))
            {
                ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Destination category not found");
                return;
            }
            destinationurl = destinationurl.Substring(req.FullPrefixUrl.Length);
            string[] destelements;
            string[] destoptions;
            if (!TrySplitURL(destinationurl, out destelements, out destoptions))
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }

            if (destelements[0] != "category")
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }
            InventoryFolder destFolder;
            var folderCache = new Dictionary<UUID, InventoryFolder>();
            try
            {
                if (!TryFindFolder(req, destelements[1], out destFolder, folderCache))
                {
                    ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Destination category not found");
                    return;
                }
            }
            catch (HttpResponse.ConnectionCloseException)
            {
                /* we need to pass it */
                throw;
            }
            catch
            {
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }

            InventoryItem item;
            if (!req.InventoryService.Item.TryGetValue(req.Agent.ID, itemid, out item))
            {
                ErrorResponse(req, HttpStatusCode.Gone, AisErrorCode.Gone, "Source item gone");
                return;
            }
            item.SetNewID(UUID.Random);
            item.ParentFolderID = destFolder.ID;

            if((item.Permissions.Current & InventoryPermissionsMask.Copy) == 0)
            {
                ErrorResponse(req, HttpStatusCode.Forbidden, AisErrorCode.QueryFailed, "Forbidden");
                return;
            }

            try
            {
                req.InventoryService.Item.Add(item);
            }
            catch
            {
                ErrorResponse(req, HttpStatusCode.Forbidden, AisErrorCode.QueryFailed, "Forbidden");
                return;
            }

            var updatedcategoryversions = new Map();
            InventoryFolder oldLocationFolder;
            if (req.InventoryService.Folder.TryGetValue(req.Agent.ID, destFolder.ID, out oldLocationFolder))
            {
                updatedcategoryversions.Add(oldLocationFolder.ID.ToString(), oldLocationFolder.Version);
            }
            if (req.InventoryService.Folder.TryGetValue(req.Agent.ID, destFolder.ID, out destFolder))
            {
                updatedcategoryversions.Add(destFolder.ID.ToString(), destFolder.Version);
            }
            item.ParentFolderID = destFolder.ID;
            Map resdata = item.ToAisV3(req.FullPrefixUrl);
            resdata.Add("_created_items", new AnArray { item.ID });
            resdata.Add("_updated_category_versions", updatedcategoryversions);

            SuccessResponse(req, HttpStatusCode.Created, resdata);
        }

        private static void ItemHandler_Move(Request req, string[] elements)
        {
            UUID itemid;

            if (!UUID.TryParse(elements[1], out itemid))
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }

            string destinationurl = req.HttpRequest["Destination"];
            if (!destinationurl.StartsWith(req.FullPrefixUrl))
            {
                ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Destination category not found");
                return;
            }
            destinationurl = destinationurl.Substring(req.FullPrefixUrl.Length);
            string[] destelements;
            string[] destoptions;
            if (!TrySplitURL(destinationurl, out destelements, out destoptions))
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }

            if (destelements[0] != "category")
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }
            InventoryFolder destFolder;
            var folderCache = new Dictionary<UUID, InventoryFolder>();
            try
            {
                if (!TryFindFolder(req, destelements[1], out destFolder, folderCache))
                {
                    ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Destination category not found");
                    return;
                }
            }
            catch (HttpResponse.ConnectionCloseException)
            {
                /* we need to pass it */
                throw;
            }
            catch
            {
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }

            InventoryItem item;
            if (!req.InventoryService.Item.TryGetValue(req.Agent.ID, itemid, out item))
            {
                ErrorResponse(req, HttpStatusCode.Gone, AisErrorCode.Gone, "Source item gone");
                return;
            }

            try
            {
                req.InventoryService.Item.Move(req.Agent.ID, itemid, destFolder.ID);
            }
            catch (InventoryItemNotFoundException)
            {
                ErrorResponse(req, HttpStatusCode.Gone, AisErrorCode.Gone, "Source item gone");
                return;
            }
            catch (InvalidParentFolderIdException)
            {
                ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Destination category not found");
                return;
            }
            catch
            {
                ErrorResponse(req, HttpStatusCode.Forbidden, AisErrorCode.QueryFailed, "Forbidden");
                return;
            }

            var updatedcategoryversions = new Map();
            InventoryFolder oldLocationFolder;
            if (req.InventoryService.Folder.TryGetValue(req.Agent.ID, item.ParentFolderID, out oldLocationFolder))
            {
                updatedcategoryversions.Add(oldLocationFolder.ID.ToString(), oldLocationFolder.Version);
            }
            if (req.InventoryService.Folder.TryGetValue(req.Agent.ID, destFolder.ID, out destFolder))
            {
                updatedcategoryversions.Add(destFolder.ID.ToString(), destFolder.Version);
            }
            item.ParentFolderID = destFolder.ID;
            Map resdata = item.ToAisV3(req.FullPrefixUrl);
            resdata.Add("_updated_items", new AnArray { item.ID });
            resdata.Add("_updated_category_versions", updatedcategoryversions);
            SuccessResponse(req, resdata);
        }

        private static void ItemHandler_Delete(Request req, string[] elements)
        {
            UUID itemid;

            if (!UUID.TryParse(elements[1], out itemid))
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }

            InventoryItem item;
            try
            {
                item = req.InventoryService.Item[req.Agent.ID, itemid];
            }
            catch (KeyNotFoundException)
            {
                ErrorResponse(req, HttpStatusCode.Gone, AisErrorCode.Gone, "Gone");
                return;
            }
            catch (HttpResponse.ConnectionCloseException)
            {
                /* we need to pass it */
                throw;
            }
            catch
            {
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }

            var data = new AISv3ResultData();
            try
            {
                DeleteItem(req, item, data);
            }
            catch
            {
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }
            SuccessResponse(req, data);
        }
        #endregion
    }
}
