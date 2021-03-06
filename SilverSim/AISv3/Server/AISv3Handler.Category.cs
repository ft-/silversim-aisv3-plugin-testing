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

using SilverSim.Main.Common.HttpServer;
using SilverSim.ServiceInterfaces.Inventory;
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
        #region Folder Handling
        private static void FolderHandler(Request req, string[] elements)
        {
            if(elements.Length == 3)
            {
                switch(elements[2])
                {
                    case "children":
                        FolderChildren_Handler(req, elements);
                        break;

                    case "links":
                        FolderLinks_Handler(req, elements);
                        break;

                    case "items":
                        FolderItems_Handler(req, elements);
                        break;

                    case "categories":
                        FolderFolders_Handler(req, elements);
                        break;

                    default:
                        ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                        break;
                }
                return;
            }

            switch (req.HttpRequest.Method)
            {
                case "GET":
                    FolderHandler_Get(req, elements);
                    break;

                case "POST":
                    FolderHandler_Post(req, elements);
                    break;

                case "PATCH":
                    FolderHandler_Patch(req, elements);
                    break;

                case "MOVE":
                    FolderHandler_Move(req, elements);
                    break;

                case "COPY":
                    FolderHandler_Copy(req, elements);
                    break;

                case "DELETE":
                    FolderHandler_Delete(req, elements);
                    break;

                default:
                    ErrorResponse(req, HttpStatusCode.MethodNotAllowed, AisErrorCode.MethodNotAllowed, "Method not allowed");
                    break;
            }
        }

        private class FolderGetStack
        {
            public int Depth;
            public InventoryFolderContent Content;
            public Map Embedded;

            public FolderGetStack(InventoryFolderContent content, Map embedded, int depth)
            {
                Content = content;
                Depth = depth;
                Embedded = embedded;
            }
        }

        private static void FolderHandler_Get(Request req, string[] elements)
        {
            var folderCache = new Dictionary<UUID, InventoryFolder>();
            InventoryFolder thisFolder;
            if (!TryFindFolder(req, elements[1], out thisFolder, folderCache))
            {
                ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Not Found");
                return;
            }
            InventoryFolderContent content;
            if (!req.InventoryService.Folder.Content.TryGetValue(req.Agent.ID, thisFolder.ID, out content))
            {
                ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Not Found");
                return;
            }

            Map resdata = thisFolder.ToAisV3(req.FullPrefixUrl);

            if (req.Depth > 0)
            {
                var stack = new List<FolderGetStack>();
                stack.Add(new FolderGetStack(content, resdata, 1));
                while (stack.Count != 0)
                {
                    FolderGetStack e = stack[0];
                    stack.RemoveAt(0);
                    if (e.Depth > req.Depth)
                    {
                        continue;
                    }

                    var embeddeditems = new Map();
                    var embeddedlinks = new Map();
                    var embeddedcategories = new Map();
                    var embedded = new Map
                    {
                        ["items"] = embeddeditems,
                        ["categories"] = embeddedcategories,
                        ["links"] = embeddedlinks
                    };
                    e.Embedded.Add("_embedded", embedded);
                    foreach (InventoryItem item in e.Content.Items)
                    {
                        if (item.AssetType == AssetType.Link || item.AssetType == AssetType.LinkFolder)
                        {
                            embeddedlinks.Add(item.ID.ToString(), item.ToAisV3(req.FullPrefixUrl));
                        }
                        else
                        {
                            embeddeditems.Add(item.ID.ToString(), item.ToAisV3(req.FullPrefixUrl));
                        }
                    }
                    foreach(InventoryFolder folder in e.Content.Folders)
                    {
                        Map folderdata = folder.ToAisV3(req.FullPrefixUrl);
                        embeddedcategories.Add(folder.ID.ToString(), folderdata);
                        if (req.InventoryService.Folder.Content.TryGetValue(req.Agent.ID, thisFolder.ID, out content))
                        {
                            stack.Add(new FolderGetStack(content, folderdata, e.Depth + 1));
                        }
                    }
                }
            }

            SuccessResponse(req, resdata);
        }

        private static void FolderHandler_Copy(Request req, string[] elements)
        {
            UUID folderid;

            if (!UUID.TryParse(elements[1], out folderid))
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
            catch(Exception e)
            {
                m_Log.Debug("Exception occured", e);
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }

            InventoryTree tree;
            try
            {
                tree = req.InventoryService.Folder.Copy(req.Agent.ID, folderid, destFolder.ID);
            }
            catch (InventoryFolderNotFoundException)
            {
                ErrorResponse(req, HttpStatusCode.Gone, AisErrorCode.Gone, "Source category gone");
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

            Map resdata = tree.ToAisV3(req.FullPrefixUrl);
            var created_items = new AnArray();
            var created_categories = new AnArray();
            var updatedcategoryversions = new Map();
            resdata.Add("_created_categories", created_categories);
            resdata.Add("_created_items", created_items);
            resdata.Add("_updated_category_versions", updatedcategoryversions);
            var stack = new List<KeyValuePair<Map, InventoryTree>>
            {
                new KeyValuePair<Map, InventoryTree>(resdata, tree)
            };
            created_categories.Add(tree.ID);
            while(stack.Count > 0)
            {
                KeyValuePair<Map, InventoryTree> kvp = stack[0];
                stack.RemoveAt(0);

                var embeddeditems = new Map();
                var embeddedlinks = new Map();
                var embeddedcategories = new Map();
                var embedded = new Map
                {
                    ["items"] = embeddeditems,
                    ["categories"] = embeddedcategories,
                    ["links"] = embeddedlinks
                };
                kvp.Key.Add("_embedded", embedded);
                foreach (InventoryItem item in kvp.Value.Items)
                {
                    if (item.AssetType == AssetType.Link || item.AssetType == AssetType.LinkFolder)
                    {
                        embeddedlinks.Add(item.ID.ToString(), item.ToAisV3(req.FullPrefixUrl));
                    }
                    else
                    {
                        embeddeditems.Add(item.ID.ToString(), item.ToAisV3(req.FullPrefixUrl));
                    }
                    created_items.Add(item.ID);
                }
                foreach (InventoryTree folder in kvp.Value.Folders)
                {
                    Map folderdata = folder.ToAisV3(req.FullPrefixUrl);
                    embeddedcategories.Add(folder.ID.ToString(), folderdata);
                    created_categories.Add(folder.ID);
                    stack.Add(new KeyValuePair<Map, InventoryTree>(folderdata, folder));
                }

            }
            if (req.InventoryService.Folder.TryGetValue(req.Agent.ID, destFolder.ID, out destFolder))
            {
                updatedcategoryversions.Add(destFolder.ID.ToString(), destFolder.Version);
            }
            SuccessResponse(req, HttpStatusCode.Created, resdata);
        }

        private static void FolderHandler_Move(Request req, string[] elements)
        {
            UUID folderid;

            if (!UUID.TryParse(elements[1], out folderid))
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
            catch(Exception e)
            {
                m_Log.Debug("Exception occured", e);
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }

            InventoryFolder folderToMove;
            if (!req.InventoryService.Folder.TryGetValue(req.Agent.ID, folderid, out folderToMove))
            {
                ErrorResponse(req, HttpStatusCode.Gone, AisErrorCode.Gone, "Source category gone");
                return;
            }

            try
            {
                req.InventoryService.Folder.Move(req.Agent.ID, folderid, destFolder.ID);
            }
            catch(InventoryFolderNotFoundException)
            {
                ErrorResponse(req, HttpStatusCode.Gone, AisErrorCode.Gone, "Source category gone");
                return;
            }
            catch(InvalidParentFolderIdException)
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
            if (req.InventoryService.Folder.TryGetValue(req.Agent.ID, folderToMove.ParentFolderID, out oldLocationFolder))
            {
                updatedcategoryversions.Add(oldLocationFolder.ID.ToString(), oldLocationFolder.Version);
            }
            if (req.InventoryService.Folder.TryGetValue(req.Agent.ID, destFolder.ID, out destFolder))
            {
                updatedcategoryversions.Add(destFolder.ID.ToString(), destFolder.Version);
            }
            folderToMove.ParentFolderID = destFolder.ID;
            Map resdata = folderToMove.ToAisV3(req.FullPrefixUrl);
            resdata.Add("_updated_categories", new AnArray { folderToMove.ID });
            resdata.Add("_updated_category_versions", updatedcategoryversions);
            SuccessResponse(req, resdata);
        }

        private static void FolderHandler_Patch(Request req, string[] elements)
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

            string name;
            IValue val;
            if (!reqmap.TryGetValue("name", out val))
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }
            name = val.ToString();

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

            folder.Name = name;
            try
            {
                req.InventoryService.Folder.Update(folder);
            }
            catch(InventoryFolderNotFoundException)
            {
                ErrorResponse(req, HttpStatusCode.Gone, AisErrorCode.Gone, "Category gone");
                return;
            }
            catch (Exception e)
            {
                m_Log.Debug("Exception occured", e);
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }
            SuccessResponse(req, folder.ToAisV3(req.FullPrefixUrl));
        }

        private static void FolderHandler_Delete(Request req, string[] elements)
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

            if(!req.InventoryService.Folder.ContainsKey(req.Agent.ID, folder.ID))
            {
                ErrorResponse(req, HttpStatusCode.Gone, AisErrorCode.Gone, "Category gone");
                return;
            }

            var result = new AISv3ResultData();
            DeleteFolder(req, folder.ID, result);
            SuccessResponse(req, result);
        }
        #endregion
    }
}
