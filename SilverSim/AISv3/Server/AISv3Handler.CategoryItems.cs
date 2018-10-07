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
using System;
using System.Collections.Generic;
using System.Net;

namespace SilverSim.AISv3.Server
{
    public static partial class AISv3Handler
    {
        private static void FolderItems_Handler(Request req, string[] elements)
        {
            switch (req.HttpRequest.Method)
            {
                case "GET":
                    FolderItems_GetHandler(req, elements);
                    break;

                case "DELETE":
                    FolderItems_DeleteHandler(req, elements);
                    break;

                default:
                    ErrorResponse(req, HttpStatusCode.MethodNotAllowed, AisErrorCode.MethodNotAllowed, "Method not allowed");
                    break;
            }
        }

        private static void FolderItems_GetHandler(Request req, string[] elements)
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
            var reflinks = new Map();
            string self = GetFolderHref(req, folder, folderCache);
            reflinks.Add("self", new Map { { "href", self } });
            reflinks.Add("parent", new Map { { "href", req.FullPrefixUrl + "/category/" + folder.ParentFolderID.ToString() } });
            reflinks.Add("links", new Map { { "href", self + "/links" } });
            reflinks.Add("items", new Map { { "href", self + "/items" } });
            reflinks.Add("children", new Map { { "href", self + "/children" } });

            var res = new Map
            {
                { "_links", reflinks },
                { "_base_uri", self },
                { "name", folder.Name },
                { "type_default", (int)folder.DefaultType },
                { "parent_id", folder.ParentFolderID },
                { "version", folder.Version },
                { "agent_id", folder.Owner.ID },
                { "category_id", folder.ID },
                {
                    "_embedded", new Map
                    {
                        { "items", items },
                        { "links", links }
                    }
                }
            };

            foreach(InventoryItem item in folderitems)
            {
                Map itemdata = item.ToAisV3(req.FullPrefixUrl);
                if (item.AssetType == AssetType.Link || item.AssetType == AssetType.LinkFolder)
                {
                    links.Add(item.ID.ToString(), itemdata);
                }
                else
                {
                    items.Add(item.ID.ToString(), itemdata);
                }
            }
            SuccessResponse(req, res);
        }

        private static void FolderItems_DeleteHandler(Request req, string[] elements)
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
            catch (Exception e)
            {
                m_Log.Debug("Exception occured", e);
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }

            List<InventoryItem> items = req.InventoryService.Folder.GetItems(req.Agent.ID, folder.ID);
            var ids = new List<UUID>();
            foreach(InventoryItem item in items)
            {
                ids.Add(item.ID);
            }

            List<UUID> deleted = req.InventoryService.Item.Delete(req.Agent.ID, ids);

            var activegestures = new AnArray();
            var brokenlinks = new AnArray();
            var category_items_removed = new AnArray();
            var updated_category_versions = new Map();

            foreach(InventoryItem item in items)
            {
                if(!ids.Contains(item.ID))
                {
                    continue;
                }
                if(item.AssetType == AssetType.Gesture &&
                    (item.Flags & InventoryFlags.GestureActive) != 0)
                {
                    activegestures.Add(item.ID);
                }
                if(item.AssetType == AssetType.Link || item.AssetType == AssetType.LinkFolder)
                {
                    brokenlinks.Add(item.ID);
                }
                category_items_removed.Add(item.ID);
            }

            if (req.InventoryService.Folder.TryGetValue(folder.ID, out folder))
            {
                updated_category_versions.Add(folder.ID.ToString(), folder.Version);
            }

            try
            {
                req.InventoryService.Folder.Purge(req.Agent.ID, folder.ID);
            }
            catch (InventoryFolderNotFoundException)
            {
                ErrorResponse(req, HttpStatusCode.Gone, AisErrorCode.Gone, "Category gone");
                return;
            }
            SuccessResponse(req, new Map
            {
                ["_attachments_removed"] = new Map(),
                ["_active_gestures_removed"] = activegestures,
                ["_broken_links_removed"] = brokenlinks,
                ["_wearables_removed"] = new Map(),
                ["_category_items_removed"] = category_items_removed,
                ["_updated_category_versions"] = updated_category_versions
            });
        }
    }
}
