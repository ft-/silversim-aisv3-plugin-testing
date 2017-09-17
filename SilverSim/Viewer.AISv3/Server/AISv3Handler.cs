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

namespace SilverSim.Viewer.AISv3.Server
{
    public static partial class AISv3Handler
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
            public bool IsSimulate;
            public int Depth;

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

        private static void SuccessResponse(Request req, HttpStatusCode statuscode, Map m)
        {
            using (var ms = new MemoryStream())
            {
                LlsdXml.Serialize(m, ms);
                byte[] buffer = ms.ToArray();
                using (HttpResponse res = req.HttpRequest.BeginResponse(statuscode, statuscode.ToString()))
                {
                    res.ContentType = "application/llsd+xml";
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

        private static bool TryGetFolder(Request request, UUID folderId, out InventoryFolder folder, Dictionary<UUID, InventoryFolder> folderCache)
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

        private static string GetFolderHref(Request request, UUID folderId, Dictionary<UUID, InventoryFolder> folderCache)
        {
            InventoryFolder folder;
            if(TryGetFolder(request, folderId, out folder, folderCache))
            {
                return GetFolderHref(request, folder, folderCache);
            }
            return request.FullPrefixUrl + "/category/unknown";
        }

        private static string GetFolderHref(Request request, InventoryFolder folder, Dictionary<UUID, InventoryFolder> folderCache)
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
            string[] elements;
            string[] options;
            if(!TrySplitURL(requrl, out elements, out options))
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }

            foreach(string option in options)
            {
                if(option.StartsWith("depth="))
                {
                    string optval = option.Substring(6);
                    if(optval == "*")
                    {
                        req.Depth = int.MaxValue;
                    }
                    else
                    {
                        req.Depth = int.Parse(optval);
                    }
                }
                else if(option.StartsWith("simulate="))
                {
                    string optval = option.Substring(9);
                    req.IsSimulate = optval == "true" || optval == "1";
                }
            }

            switch(elements[3])
            {
                case "item":
                    try
                    {
                        ItemHandler(req, elements);
                    }
                    catch
                    {
                        ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                    }
                    break;

                case "category":
                    try
                    {
                        FolderHandler(req, elements);
                    }
                    catch
                    {
                        ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                    }
                    break;

                default:
                    ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Not Found");
                    break;
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
            if (splitquery.Length < 2)
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
    }
}
