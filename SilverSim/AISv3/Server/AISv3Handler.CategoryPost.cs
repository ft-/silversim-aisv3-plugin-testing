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
        private static void FolderHandler_Post(Request req, string[] elements)
        {
            if (req.HttpRequest.ContentType != "application/llsd+xml")
            {
                ErrorResponse(req, HttpStatusCode.UnsupportedMediaType, AisErrorCode.UnsupportedMedia, "Unsupported media type");
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
            Map resdata = folder.ToAisV3(req.FullPrefixUrl);
            resdata.Add("_embedded", reqmap);
            var created_categories = new AnArray();
            var created_items = new AnArray();
            var updated_categories = new AnArray();
            resdata.Add("_created_categories", created_categories);
            resdata.Add("_updated_category_versions", updated_categories);
            resdata.Add("_created_items", created_items);
            var resfolders = new Map();

            var stack = new List<Map>
            {
                resdata
            };
            while (stack.Count > 0)
            {
                Map processFolder = stack[0];
                stack.RemoveAt(0);

                Map embedded;
                UUID toParentFolderId = processFolder["category_id"].AsUUID;
                if(processFolder.TryGetValue("_embedded", out embedded))
                {
                    AnArray items;
                    if(embedded.TryGetValue("items", out items))
                    {
                        var newitems = new Map();
                        foreach(IValue itemiv in items)
                        {
                            var itemdata = itemiv as Map;
                            if(itemdata == null)
                            {
                                continue;
                            }
                            try
                            {
                                InventoryItem item = itemdata.ItemFromAisV3(req.Agent, toParentFolderId, req.FullPrefixUrl);
                                req.InventoryService.Item.Add(item);
                                created_items.Add(item.ID);
                                if (!updated_categories.Contains(toParentFolderId))
                                {
                                    updated_categories.Add(toParentFolderId);
                                }
                                newitems.Add(item.ID.ToString(), itemdata);
                            }
                            catch
                            {
                                /* intentionally left empty */
                            }
                        }
                        embedded["items"] = newitems;
                    }
                    AnArray links;
                    if(embedded.TryGetValue("links", out links))
                    {
                        var newlinks = new Map();
                        foreach (IValue linkiv in links)
                        {
                            var linkdata = linkiv as Map;
                            if (linkdata == null)
                            {
                                continue;
                            }
                            try
                            {
                                InventoryItem link = linkdata.ItemFromAisV3(req.Agent, toParentFolderId, req.FullPrefixUrl);
                                req.InventoryService.Item.Add(link);
                                created_items.Add(link.ID);
                                if (!updated_categories.Contains(toParentFolderId))
                                {
                                    updated_categories.Add(toParentFolderId);
                                }
                                newlinks.Add(link.ID.ToString(), link.ToAisV3(req.FullPrefixUrl));
                            }
                            catch
                            {
                                /* intentionally left empty */
                            }
                        }
                        embedded["links"] = newlinks;
                    }
                    AnArray categories;
                    if(embedded.TryGetValue("categories", out categories))
                    {
                        var newcategories = new Map();
                        foreach (IValue categoryiv in categories)
                        {
                            var categorydata = categoryiv as Map;
                            if (categorydata == null)
                            {
                                continue;
                            }
                            try
                            {
                                InventoryFolder newfolder = categorydata.CategoryFromAisV3(req.Agent, toParentFolderId, req.FullPrefixUrl);
                                req.InventoryService.Folder.Add(newfolder);
                                created_categories.Add(newfolder.ID);
                                stack.Add(categorydata);
                                if (!updated_categories.Contains(toParentFolderId))
                                {
                                    updated_categories.Add(toParentFolderId);
                                }
                                newcategories.Add(newfolder.ID.ToString(), categorydata);
                            }
                            catch
                            {
                                /* intentionally left empty */
                            }
                        }
                        embedded["categories"] = newcategories;
                    }
                }
            }

            SuccessResponse(req, HttpStatusCode.Created, resdata, req.FullPrefixUrl + "/category/" + folder.ID.ToString() + "/children");
        }
    }
}
