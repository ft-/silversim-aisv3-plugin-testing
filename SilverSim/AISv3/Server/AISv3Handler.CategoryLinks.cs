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
using SilverSim.Types;
using SilverSim.Types.Asset;
using SilverSim.Types.Inventory;
using SilverSim.Types.StructuredData.Llsd;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace SilverSim.AISv3.Server
{
    public static partial class AISv3Handler
    {
        private static void FolderLinks_Handler(Request req, string[] elements)
        {
            switch (req.HttpRequest.Method)
            {
                case "GET":
                    FolderLinks_GetHandler(req, elements);
                    break;

                case "DELETE":
                    FolderLinks_DeleteHandler(req, elements);
                    break;

                default:
                    ErrorResponse(req, HttpStatusCode.MethodNotAllowed, AisErrorCode.MethodNotAllowed, "Method not allowed");
                    break;
            }
        }

        private static void FolderLinks_GetHandler(Request req, string[] elements)
        {
            InventoryFolder folder;
            var folderCache = new Dictionary<UUID, InventoryFolder>();
            try
            {
                if (!TryFindFolder(req, elements[1], out folder, folderCache))
                {
                    ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Not Found");
                    return;
                }
            }
            catch (HttpResponse.ConnectionCloseException)
            {
                /* we need to pass it */
                throw;
            }
            catch (Exception e)
            {
                m_Log.Debug("Exception occured", e);
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }

            List<InventoryItem> folderitems = req.InventoryService.Folder.GetItems(req.Agent.ID, folder.ID);
            var ids = new List<UUID>();

            var items = new Map();
            var links = new Map();
            var res = folder.ToAisV3(req.FullPrefixUrl);
            res.Add("_base_uri", GetFolderHref(req, folder.ID, folderCache).ToAisV3Href() );
            res.Add("_embedded", new Map
            {
                { "items", items },
                { "links", links }
            });

            foreach (InventoryItem item in folderitems)
            {
                Map itemdata = item.ToAisV3(req.FullPrefixUrl);
                if (item.AssetType == AssetType.Link || item.AssetType == AssetType.LinkFolder)
                {
                    var linkembedded = new Map();
                    itemdata.Add("_embedded", linkembedded);
                    if (item.AssetType == AssetType.Link)
                    {
                        InventoryItem linkeditem;
                        if(req.InventoryService.Item.TryGetValue(req.Agent.ID, item.AssetID, out linkeditem))
                        {
                            Map linkeditemdata = linkeditem.ToAisV3(req.FullPrefixUrl);
                            linkembedded.Add("item", linkeditemdata);
                        }
                    }
                    else
                    {
                        InventoryFolder linkedfolder;
                        if (req.InventoryService.Folder.TryGetValue(req.Agent.ID, item.AssetID, out linkedfolder))
                        {
                            Map linkedfolderdata = linkedfolder.ToAisV3(req.FullPrefixUrl);
                            linkembedded.Add("item", linkedfolderdata);
                        }
                    }
                    items.Add(item.ID.ToString(), itemdata);
                }
                else
                {
                    links.Add(item.ID.ToString(), itemdata);
                }
            }
            SuccessResponse(req, res);
        }

        private static void FolderLinks_PutHandler(Request req, string[] elements)
        {
            AnArray newlinks;
            try
            {
                using (Stream s = req.HttpRequest.Body)
                {
                    newlinks = (AnArray)LlsdXml.Deserialize(s);
                }
            }
            catch
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }

            InventoryFolder folder;
            var folderCache = new Dictionary<UUID, InventoryFolder>();
            try
            {
                if (!TryFindFolder(req, elements[1], out folder, folderCache))
                {
                    ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Not Found");
                    return;
                }
            }
            catch (HttpResponse.ConnectionCloseException)
            {
                /* we need to pass it */
                throw;
            }
            catch (Exception e)
            {
                m_Log.Debug("Exception occured", e);
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }

            List<InventoryItem> items = req.InventoryService.Folder.GetItems(req.Agent.ID, folder.ID);
            var ids = new List<UUID>();
            foreach (InventoryItem item in items)
            {
                if (item.AssetType == AssetType.Link || item.AssetType == AssetType.LinkFolder)
                {
                    ids.Add(item.ID);
                }
            }

            List<UUID> deleted = req.InventoryService.Item.Delete(req.Agent.ID, ids);

            var category_items_removed = new AnArray();
            var updated_category_versions = new Map();

            foreach (InventoryItem item in items)
            {
                if (!ids.Contains(item.ID))
                {
                    continue;
                }
                category_items_removed.Add(item.ID);
            }

            var links = new Map();
            var addeditems = new AnArray();
            var linkedids = new Map();
            foreach(IValue iv in newlinks)
            {
                var newlink = iv as Map;
                if(newlink == null)
                {
                    continue;
                }
                var item = new InventoryItem(UUID.Random)
                {
                    AssetID = newlink["linked_id"].AsUUID,
                    Name = newlink["name"].ToString(),
                    Description = newlink["desc"].ToString(),
                    AssetType = (AssetType)newlink["type"].AsInt
                };
                InventoryItem linkeditem;
                InventoryFolder linkedfolder;
                Map itemdata;
                if(item.AssetType == AssetType.Link && req.InventoryService.Item.TryGetValue(req.Agent.ID, item.AssetID, out linkeditem))
                {
                    item.InventoryType = linkeditem.InventoryType;
                    itemdata = item.ToAisV3(req.FullPrefixUrl);
                    var embedded = new Map();
                    embedded.Add("item", linkeditem.ToAisV3(req.FullPrefixUrl));
                    itemdata.Add("_embedded", embedded);
                    links.Add(item.ID.ToString(), itemdata);
                    req.InventoryService.Item.Add(item);
                    addeditems.Add(item.ID);
                    linkedids.Add(item.ID.ToString(), item.AssetID.ToString());
                }
                else if(item.AssetType == AssetType.LinkFolder && req.InventoryService.Folder.TryGetValue(req.Agent.ID, item.AssetID, out linkedfolder))
                {
                    item.InventoryType = InventoryType.Unknown;
                    itemdata = item.ToAisV3(req.FullPrefixUrl);
                    var embedded = new Map();
                    embedded.Add("category", linkedfolder.ToAisV3(req.FullPrefixUrl));
                    itemdata.Add("_embedded", embedded);
                    links.Add(item.ID.ToString(), itemdata);
                    req.InventoryService.Item.Add(item);
                    addeditems.Add(item.ID);
                    linkedids.Add(item.ID.ToString(), item.AssetID.ToString());
                }
            }

            if (req.InventoryService.Folder.TryGetValue(req.Agent.ID, folder.ID, out folder))
            {
                updated_category_versions.Add(folder.ID.ToString(), folder.Version);
            }

            SuccessResponse(req, new Map
            {
                ["_embedded"] = new Map { { "links", links } },
                ["_linked_ids"] = linkedids,
                ["_created_items"] = addeditems,
                ["_category_items_removed"] = category_items_removed,
                ["_updated_category_versions"] = updated_category_versions
            });
        }

        private static void FolderLinks_DeleteHandler(Request req, string[] elements)
        {
            InventoryFolder folder;
            var folderCache = new Dictionary<UUID, InventoryFolder>();
            try
            {
                if (!TryFindFolder(req, elements[1], out folder, folderCache))
                {
                    ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Not Found");
                    return;
                }
            }
            catch (HttpResponse.ConnectionCloseException)
            {
                /* we need to pass it */
                throw;
            }
            catch (Exception e)
            {
                m_Log.Debug("Exception occured", e);
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }

            List<InventoryItem> items = req.InventoryService.Folder.GetItems(req.Agent.ID, folder.ID);
            var ids = new List<UUID>();
            foreach (InventoryItem item in items)
            {
                if (item.AssetType == AssetType.Link || item.AssetType == AssetType.LinkFolder)
                {
                    ids.Add(item.ID);
                }
            }

            List<UUID> deleted = req.InventoryService.Item.Delete(req.Agent.ID, ids);

            var category_items_removed = new AnArray();
            var updated_category_versions = new Map();

            foreach (InventoryItem item in items)
            {
                if (!ids.Contains(item.ID))
                {
                    continue;
                }
                category_items_removed.Add(item.ID);
            }

            if (req.InventoryService.Folder.TryGetValue(req.Agent.ID, folder.ID, out folder))
            {
                updated_category_versions.Add(folder.ID.ToString(), folder.Version);
            }

            SuccessResponse(req, new Map
            {
                ["_category_items_removed"] = category_items_removed,
                ["_updated_category_versions"] = updated_category_versions
            });
        }
    }
}
