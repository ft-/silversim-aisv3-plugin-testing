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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

namespace SilverSim.Viewer.AISv3
{
    public static class AISv3Handler
    {
        private enum AisErrorCode
        {
            InvalidRequest = 0,
            InvalidShape = 1,
            InvalidDepth = 2,
            BrokenLink = 3,
            NotFound = 4,
            AgentNotFound = 5,
            NoInventoryRoot = 6,
            MethodNotAllowed = 7,
            Conflict = 8,
            Gone = 9,
            ConditionFailed = 10,
            InternalError = 11,
            QueryFailed = 12,
            QueryExpectationFailed = 13,
            InvalidPermissions = 14,
            NotSupported = 15,
            Unknown = 16,
            UnsupportedMedia = 17
        }

        public class Request
        {
            public readonly HttpRequest HttpRequest;
            public readonly InventoryServiceInterface InventoryService;
            public readonly UUI Agent;
            public readonly bool IsLibrary;
            public string RawPrefixUrl;
            public string FullPrefixUrl;

            public Request(HttpRequest req, InventoryServiceInterface inventoryService, UUI agent, bool isLibrary, string rawPrefixUrl, string fullPrefixUrl)
            {
                HttpRequest = req;
                InventoryService = inventoryService;
                Agent = agent;
                IsLibrary = isLibrary;
                RawPrefixUrl = rawPrefixUrl;
                FullPrefixUrl = fullPrefixUrl;
            }
        }

        private static void SuccessResponse(Request req, Map m)
        {
            using (var ms = new MemoryStream())
            {
                LlsdXml.Serialize(m, ms);
                byte[] buffer = ms.ToArray();
                using (HttpResponse res = req.HttpRequest.BeginResponse("application/llsd+xml"))
                {
                    using (Stream o = res.GetOutputStream(buffer.Length))
                    {
                        o.Write(buffer, 0, (int)buffer.Length);
                    }
                }
            }
        }

        private static void SuccessResponse(Request req)
        {
            SuccessResponse(req, new Map());
        }

        private static void ErrorResponse(Request req, HttpStatusCode code, AisErrorCode errorcode, string description)
        {
            ErrorResponse(req, code, errorcode, description, new Map());
        }

        private static void ErrorResponse(Request req, HttpStatusCode code, AisErrorCode errorcode, string description, Map m)
        {
            m.Add("error_code", (int)errorcode);
            using (var ms = new MemoryStream())
            {
                LlsdXml.Serialize(m, ms);
                using (HttpResponse res = req.HttpRequest.BeginResponse(code, description, "application/llsd+xml"))
                {
                    using (Stream o = res.GetOutputStream(ms.Length))
                    {
                        o.Write(ms.ToArray(), 0, (int)ms.Length);
                    }
                }
            }
        }

        public static bool TryGetFolder(Request request, UUID folderId, out InventoryFolder folder, Dictionary<UUID, InventoryFolder> folderCache)
        {
            if(folderCache.TryGetValue(folderId, out folder))
            {
                return true;
            }
            if(request.InventoryService.Folder.TryGetValue(request.Agent.ID, out folder))
            {
                folderCache.Add(folder.ID, folder);
                return true;
            }
            return false;
        }

        public static string GetFolderHref(Request request, UUID folderId, Dictionary<UUID, InventoryFolder> folderCache)
        {
            InventoryFolder folder;
            if(TryGetFolder(request, folderId, out folder, folderCache))
            {
                return GetFolderHref(request, folder, folderCache);
            }
            return request.FullPrefixUrl + "/category/unknown";
        }

        public static string GetFolderHref(Request request, InventoryFolder folder, Dictionary<UUID, InventoryFolder> folderCache)
        {
            InventoryFolder parentFolder;
            if(folder.ParentFolderID == UUID.Zero)
            {
                return request.FullPrefixUrl + "/category/root";
            }
            else if(TryGetFolder(request, folder.ParentFolderID, out parentFolder, folderCache) && 
                parentFolder.ParentFolderID == UUID.Zero)
            {
                switch(folder.InventoryType)
                {
                    case InventoryType.Animation:
                        return request.FullPrefixUrl + "/category/animatn";
                    case InventoryType.Bodypart:
                        return request.FullPrefixUrl + "/category/bodypart";
                    case InventoryType.Clothing:
                        return request.FullPrefixUrl + "/category/clothing";
                    case InventoryType.CurrentOutfitFolder:
                        return request.FullPrefixUrl + "/category/current";
                    case InventoryType.FavoriteFolder:
                        return request.FullPrefixUrl + "/category/favorite";
                    case InventoryType.Gesture:
                        return request.FullPrefixUrl + "/category/gesture";
                    case InventoryType.Inbox:
                        return request.FullPrefixUrl + "/category/inbox";
                    case InventoryType.Landmark:
                        return request.FullPrefixUrl + "/category/landmark";
                    case InventoryType.LSLText:
                        return request.FullPrefixUrl + "/category/lsltext";
                    case InventoryType.LostAndFoundFolder:
                        return request.FullPrefixUrl + "/category/lstndfnd";
                    case InventoryType.MyOutfitsFolder:
                        return request.FullPrefixUrl + "/category/my_otfts";
                    case InventoryType.Notecard:
                        return request.FullPrefixUrl + "/category/notecard";
                    case InventoryType.Object:
                        return request.FullPrefixUrl + "/category/object";
                    case InventoryType.Outbox:
                        return request.FullPrefixUrl + "/category/outbox";
                    case InventoryType.RootFolder:
                        return request.FullPrefixUrl + "/category/root";
                    case InventoryType.SnapshotFolder:
                        return request.FullPrefixUrl + "/category/snapshot";
                    case InventoryType.Sound:
                        return request.FullPrefixUrl + "/category/sound";
                    case InventoryType.Texture:
                        return request.FullPrefixUrl + "/category/texture";
                    case InventoryType.TrashFolder:
                        return request.FullPrefixUrl + "/category/trash";
                    default:
                        break;
                }
            }
            return request.FullPrefixUrl + "/category/" + folder.ID.ToString();
        }

        public static void MainHandler(Request req)
        {
            if (!req.HttpRequest.RawUrl.StartsWith(req.RawPrefixUrl))
            {
                req.HttpRequest.ErrorResponse(HttpStatusCode.NotFound, "Not Found");
                return;
            }

            string requrl = req.HttpRequest.RawUrl.Substring(req.RawPrefixUrl.Length);

            if(requrl.StartsWith("/item/"))
            {
                try
                {
                    ItemHandler(req, requrl);
                }
                catch
                {
                    ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                }
            }
            else if(requrl.StartsWith("/category/"))
            {
                try
                {
                    FolderHandler(req, requrl);
                }
                catch
                {
                    ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                }
            }
            else
            {
                ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Not Found");
            }
        }

        private static bool TryFindFolder(Request req, string category_id, out InventoryFolder folder, Dictionary<UUID, InventoryFolder> folderCache)
        {
            switch(category_id)
            {
                case "animatn":
                    if(!req.InventoryService.Folder.TryGetValue(req.Agent.ID, AssetType.Animation, out folder))
                    {
                        return false;
                    }
                    break;

                case "bodypart":
                    if (!req.InventoryService.Folder.TryGetValue(req.Agent.ID, AssetType.Bodypart, out folder))
                    {
                        return false;
                    }
                    break;

                case "clothing":
                    if (!req.InventoryService.Folder.TryGetValue(req.Agent.ID, AssetType.Clothing, out folder))
                    {
                        return false;
                    }
                    break;

                case "current":
                    if (!req.InventoryService.Folder.TryGetValue(req.Agent.ID, AssetType.CurrentOutfitFolder, out folder))
                    {
                        return false;
                    }
                    break;

                case "favorite":
                    if (!req.InventoryService.Folder.TryGetValue(req.Agent.ID, AssetType.FavoriteFolder, out folder))
                    {
                        return false;
                    }
                    break;

                case "gesture":
                    if (!req.InventoryService.Folder.TryGetValue(req.Agent.ID, AssetType.Gesture, out folder))
                    {
                        return false;
                    }
                    break;

                case "inbox":
                    if (!req.InventoryService.Folder.TryGetValue(req.Agent.ID, AssetType.Inbox, out folder))
                    {
                        return false;
                    }
                    break;

                case "landmark":
                    if (!req.InventoryService.Folder.TryGetValue(req.Agent.ID, AssetType.Landmark, out folder))
                    {
                        return false;
                    }
                    break;

                case "lsltext":
                    if (!req.InventoryService.Folder.TryGetValue(req.Agent.ID, AssetType.LSLText, out folder))
                    {
                        return false;
                    }
                    break;

                case "lstndfnd":
                    if (!req.InventoryService.Folder.TryGetValue(req.Agent.ID, AssetType.LostAndFoundFolder, out folder))
                    {
                        return false;
                    }
                    break;

                case "my_otfts":
                    if (!req.InventoryService.Folder.TryGetValue(req.Agent.ID, AssetType.MyOutfitsFolder, out folder))
                    {
                        return false;
                    }
                    break;

                case "notecard":
                    if (!req.InventoryService.Folder.TryGetValue(req.Agent.ID, AssetType.Notecard, out folder))
                    {
                        return false;
                    }
                    break;

                case "object":
                    if (!req.InventoryService.Folder.TryGetValue(req.Agent.ID, AssetType.Object, out folder))
                    {
                        return false;
                    }
                    break;

                case "outbox":
                    if (!req.InventoryService.Folder.TryGetValue(req.Agent.ID, AssetType.Outbox, out folder))
                    {
                        return false;
                    }
                    break;

                case "root":
                    if (!req.InventoryService.Folder.TryGetValue(req.Agent.ID, AssetType.RootFolder, out folder))
                    {
                        return false;
                    }
                    break;

                case "snapshot":
                    if (!req.InventoryService.Folder.TryGetValue(req.Agent.ID, AssetType.SnapshotFolder, out folder))
                    {
                        return false;
                    }
                    break;

                case "sound":
                    if (!req.InventoryService.Folder.TryGetValue(req.Agent.ID, AssetType.Sound, out folder))
                    {
                        return false;
                    }
                    break;

                case "texture":
                    if (!req.InventoryService.Folder.TryGetValue(req.Agent.ID, AssetType.Texture, out folder))
                    {
                        return false;
                    }
                    break;

                case "trash":
                    if (!req.InventoryService.Folder.TryGetValue(req.Agent.ID, AssetType.TrashFolder, out folder))
                    {
                        return false;
                    }
                    break;

                default:
                    UUID id;
                    if(!UUID.TryParse(category_id, out id))
                    {
                        folder = default(InventoryFolder);
                        return false;
                    }
                    if(!req.InventoryService.Folder.TryGetValue(req.Agent.ID, id, out folder))
                    {
                        return false;
                    }
                    break;
            }
            folderCache.Add(folder.ID, folder);
            return true;
        }

        private static bool TrySplitURL(string rawurl, out string[] elements, out string[] options)
        {
            string[] splitquery = rawurl.Split('?');
            elements = splitquery[0].Substring(1).Split('/');
            if (elements.Length < 2)
            {
                options = new string[0];
                return false;
            }

            if(splitquery.Length > 1)
            {
                options = splitquery[1].Split(',');
            }
            else
            {
                options = new string[0];
            }
            return true;
        }

        #region Item Handling
        private static void ItemHandler(Request req, string RawUrl)
        {
            string[] elements;
            string[] options;
            if(!TrySplitURL(RawUrl, out elements, out options) || elements.Length != 2)
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }
            switch(req.HttpRequest.Method)
            {
                case "GET":
                    ItemHandler_Get(req, elements, options);
                    break;

                case "PATCH":
                    ItemHandler_Patch(req, elements, options);
                    break;

                case "COPY":
                    ItemHandler_Copy(req, elements, options);
                    break;

                case "DELETE":
                    ItemHandler_Delete(req, elements, options);
                    break;

                default:
                    ErrorResponse(req, HttpStatusCode.MethodNotAllowed, AisErrorCode.MethodNotAllowed, "Method not allowed");
                    break;
            }
        }

        private static void ItemHandler_Get(Request req, string[] elements, string[] options)
        {
            UUID itemid;

            if (!UUID.TryParse(elements[1], out itemid))
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

            var folderCache = new Dictionary<UUID, InventoryFolder>();
            Map resmap;
            if(item.AssetType == AssetType.Link)
            {
                resmap = new Map
                {
                    { "_base_uri", req.FullPrefixUrl + "/item/" + itemid.ToString() }
                };
                var linkref = new Map();
                var href = new Map
                {
                    { "href", req.FullPrefixUrl + "/item/" + item.AssetID.ToString() }
                };
                linkref.Add("item", href);
                href = new Map
                {
                    { "href", req.FullPrefixUrl + "/item/" + item.ID.ToString() }
                };
                linkref.Add("self", href);
                href = new Map
                {
                    { "href", GetFolderHref(req, item.ParentFolderID, folderCache) }
                };
                linkref.Add("parent", href);
                InventoryItem linkeditem;
                if(req.InventoryService.Item.TryGetValue(item.AssetID, out linkeditem))
                {
                    var embmap = new Map();
                    resmap.Add("_embedded", embmap);
                    embmap.Add("item", linkeditem.ToAisV3());
                }
            }
            else if(item.AssetType == AssetType.LinkFolder)
            {
                resmap = new Map
                {
                    { "_base_uri", req.FullPrefixUrl + "/item/" + itemid.ToString() }
                };
                var linkref = new Map();
                var href = new Map
                {
                    { "href", GetFolderHref(req, item.AssetID, folderCache) }
                };
                linkref.Add("category", href);
                href = new Map
                {
                    { "href", req.FullPrefixUrl + "/item/" + item.ID.ToString() }
                };
                linkref.Add("self", href);
                href = new Map
                {
                    { "href", GetFolderHref(req, item.ParentFolderID, folderCache) }
                };
                linkref.Add("parent", href);
                InventoryFolder linkedfolder;
                if (TryGetFolder(req, item.AssetID, out linkedfolder, folderCache))
                {
                    var embmap = new Map();
                    resmap.Add("_embedded", embmap);
                    var foldermap = linkedfolder.ToAisV3();
                    embmap.Add("category", foldermap);
                    linkref = new Map();
                    foldermap.Add("_links", linkref);
                    href = new Map
                    {
                        { "href", req.FullPrefixUrl + "/category/" + linkedfolder.ID.ToString() }
                    };
                    linkref.Add("self", href);
                    href = new Map
                    {
                        { "href", GetFolderHref(req, linkedfolder.ParentFolderID, folderCache) }
                    };
                    linkref.Add("parent", href);
                }
            }
            else
            {
                resmap = item.ToAisV3();
                var linkref = new Map();
                var href = new Map
                {
                    { "href", req.FullPrefixUrl + "/item/" + itemid.ToString() }
                };
                linkref.Add("self", href);
                href = new Map
                {
                    { "href", GetFolderHref(req, item.ParentFolderID, folderCache) }
                };
                linkref.Add("parent", href);
                resmap.Add("_links", linkref);
                resmap.Add("_base_uri", req.FullPrefixUrl + "/item/" + itemid.ToString());
            }
            SuccessResponse(req, resmap);
        }

        private static void ItemHandler_Patch(Request req, string[] elements, string[] options)
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
        }

        private static void ItemHandler_Copy(Request req, string[] elements, string[] options)
        {
            UUID itemid;

            if (!UUID.TryParse(elements[1], out itemid))
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }

            string destinationurl = req.HttpRequest["Destination"];
            if (!destinationurl.StartsWith(req.RawPrefixUrl))
            {
                ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Destination category not found");
                return;
            }
            destinationurl = destinationurl.Substring(req.RawPrefixUrl.Length);
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

            try
            {
                req.InventoryService.Item.Add(item);
            }
            catch
            {
                ErrorResponse(req, HttpStatusCode.Forbidden, AisErrorCode.QueryFailed, "Forbidden");
                return;
            }
            SuccessResponse(req);
        }

        private static void ItemHandler_Delete(Request req, string[] elements, string[] options)
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
            catch
            {
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }
            try
            {
                req.InventoryService.Item.Delete(req.Agent.ID, item.ID);
            }
            catch
            {
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }
            SuccessResponse(req);
        }
        #endregion

        #region Folder Handling
        private static void FolderHandler(Request req, string RawUrl)
        {
            string[] elements;
            string[] options;
            if (!TrySplitURL(RawUrl, out elements, out options))
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }

            switch(req.HttpRequest.Method)
            {
                case "GET":
                    if (elements.Length == 2)
                    {
                        FolderHandler_Get(req, elements, options);
                    }
                    else if(elements.Length == 3)
                    {
                        switch(elements[2])
                        {
                            case "chilren":
                                break;

                            case "links":
                                break;

                            case "items":
                                break;

                            case "categories":
                                break;

                            default:
                                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Invalid request");
                                break;
                        }
                    }
                    break;

                case "POST":
                    FolderHandler_Post(req, elements, options);
                    break;

                case "PUT":
                    FolderHandler_Put(req, elements, options);
                    break;

                case "PATCH":
                    FolderHandler_Patch(req, elements, options);
                    break;

                case "DELETE":
                    FolderHandler_Delete(req, elements, options);
                    break;

                default:
                    ErrorResponse(req, HttpStatusCode.MethodNotAllowed, AisErrorCode.MethodNotAllowed, "Method not allowed");
                    break;
            }
        }

        private static void FolderHandler_Get(Request req, string[] elements, string[] options)
        {
            var folderCache = new Dictionary<UUID, InventoryFolder>();
            InventoryFolder thisFolder;
            if (!TryFindFolder(req, elements[1], out thisFolder, folderCache))
            {
                ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Not Found");
                return;
            }
            InventoryFolderContent content;
            if(!req.InventoryService.Folder.Content.TryGetValue(req.Agent.ID, thisFolder.ID, out content))
            {
                ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Not Found");
                return;
            }


        }

        private static void FolderHandler_Post(Request req, string[] elements, string[] options)
        {
            if(req.HttpRequest.ContentType != "application/llsd+xml")
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
                if(!TryFindFolder(req, elements[1], out folder, folderCache))
                {
                    ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Not Found");
                    return;
                }
            }
            catch (Exception)
            {
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }

            List<InventoryItem> items;
            var itemlinks = new List<InventoryItem>();
            var folders = new List<InventoryFolder>();
            AnArray array;

            items = reqmap.TryGetValue("items", out array) ?
                items = array.ItemsFromAisV3(req.Agent, folder.ID) :
                new List<InventoryItem>();

            if(reqmap.TryGetValue<AnArray>("links", out array))
            {
                List<InventoryItem> links = array.LinksFromAisV3(req.Agent, folder.ID);
                items.AddRange(from link in links where link.AssetType == AssetType.LinkFolder select link);
                itemlinks.AddRange(from link in links where link.AssetType == AssetType.Link select link);
            }

            if(reqmap.TryGetValue<AnArray>("categories", out array))
            {
                array.CategoriesFromAisV3(req.Agent, folder.ID, folders, items, itemlinks);
            }

            /* linkfolder entries do not need specific handling */
            var linked_ids = new List<UUID>(from link in itemlinks select link.AssetID);
            var dedup_linked_ids = new List<UUID>();
            var rescreateditems = new AnArray();
            foreach(UUID id in linked_ids)
            {
                if(!dedup_linked_ids.Contains(id))
                {
                    dedup_linked_ids.Add(id);
                }
            }
            var linkeditems = new Dictionary<UUID, InventoryItem>();
            foreach(InventoryItem item in 
                from linkeditem in req.InventoryService.Item[req.Agent.ID, dedup_linked_ids] select linkeditem)
            {
                linkeditems.Add(item.ID, item);
                rescreateditems.Add(item.ID);
            }

            foreach(InventoryItem item in itemlinks)
            {
                InventoryItem linkeditem;
                if(linkeditems.TryGetValue(item.AssetID, out linkeditem))
                {
                    item.InventoryType = linkeditem.InventoryType;
                    items.Add(item);
                    rescreateditems.Add(item.ID);
                }
            }

            try
            {
                foreach (InventoryFolder folderentry in folders)
                {
                    req.InventoryService.Folder.Add(folderentry);
                }
            }
            catch
            {
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }
            try
            {
                foreach(InventoryItem item in items)
                {
                    req.InventoryService.Item.Add(item);
                }
            }
            catch
            {
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }

            var resmap = new Map();
            var resarray = new AnArray();
            foreach(InventoryFolder folderentry in folders)
            {
                resarray.Add(folderentry.ID);
            }
            resmap.Add("_created_categories", resarray);
            resmap.Add("_created_items", rescreateditems);
            SuccessResponse(req, resmap);
        }

        private static void FolderHandler_Put(Request req, string[] elements, string[] options)
        {
            if (req.HttpRequest.ContentType != "application/llsd+xml")
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.UnsupportedMedia, "Unsupported media type");
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
        }

        private static void FolderHandler_Patch(Request req, string[] elements, string[] options)
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
                if(!TryFindFolder(req, elements[1], out folder, folderCache))
                {
                    ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Not Found");
                    return;
                }
            }
            catch (Exception)
            {
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }

            folder.Name = name;
            try
            {
                req.InventoryService.Folder.Update(folder);
            }
            catch(Exception)
            {
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }
            SuccessResponse(req);
        }

        private static void FolderHandler_Delete(Request req, string[] elements, string[] options)
        {
            InventoryFolder folder;
            var folderCache = new Dictionary<UUID, InventoryFolder>();
            try
            {
                if(!TryFindFolder(req, elements[1], out folder, folderCache))
                {
                    ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Not Found");
                    return;
                }
            }
            catch (Exception)
            {
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }

            if (elements.Length == 2)
            {
                if (folder.InventoryType != InventoryType.Unknown)
                {
                    ErrorResponse(req, HttpStatusCode.Forbidden, AisErrorCode.NotSupported, "Forbidden");
                    return;
                }
                try
                {
                    req.InventoryService.Folder.Delete(req.Agent.ID, folder.ID);
                }
                catch (KeyNotFoundException)
                {
                    ErrorResponse(req, HttpStatusCode.Gone, AisErrorCode.Gone, "Category gone");
                    return;
                }
                SuccessResponse(req);
            }
            else if(elements.Length == 3 && elements[2] == "children")
            {
                try
                {
                    req.InventoryService.Folder.Purge(req.Agent.ID, folder.ID);
                }
                catch (KeyNotFoundException)
                {
                    ErrorResponse(req, HttpStatusCode.Gone, AisErrorCode.Gone, "Category gone");
                    return;
                }
                SuccessResponse(req);
            }
            else
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.NotSupported, "Bad request");
            }
        }
        #endregion
    }
}
