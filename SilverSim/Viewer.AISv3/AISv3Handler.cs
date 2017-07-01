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
    public class AISv3Handler
    {
        /* the folderCache is only used per-request to prevent unnecessary requests to inventory service */
        private string m_PrefixUrl;
        private int m_PrefixUrlLength;
        private InventoryServiceInterface m_InventoryService;
        private UUI m_Agent;

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

        public AISv3Handler(string prefixurl, InventoryServiceInterface inventoryService, UUI agent)
        {
            m_PrefixUrl = prefixurl;
            m_PrefixUrlLength = m_PrefixUrl.Length;
            m_InventoryService = inventoryService;
            m_Agent = agent;
        }

        private void SuccessResponse(HttpRequest req, Map m)
        {
            using (var ms = new MemoryStream())
            {
                LlsdXml.Serialize(m, ms);
                using (HttpResponse res = req.BeginResponse("application/llsd+xml"))
                {
                    using (Stream o = res.GetOutputStream(ms.Length))
                    {
                        o.Write(ms.ToArray(), 0, (int)ms.Length);
                    }
                }
            }
        }

        private void SuccessResponse(HttpRequest req)
        {
            SuccessResponse(req, new Map());
        }

        private void ErrorResponse(HttpRequest req, HttpStatusCode code, AisErrorCode errorcode, string description)
        {
            ErrorResponse(req, code, errorcode, description, new Map());
        }

        private void ErrorResponse(HttpRequest req, HttpStatusCode code, AisErrorCode errorcode, string description, Map m)
        {
            m.Add("error_code", (int)errorcode);
            using (var ms = new MemoryStream())
            {
                LlsdXml.Serialize(m, ms);
                using (HttpResponse res = req.BeginResponse(code, description, "application/llsd+xml"))
                {
                    using (Stream o = res.GetOutputStream(ms.Length))
                    {
                        o.Write(ms.ToArray(), 0, (int)ms.Length);
                    }
                }
            }
        }

        public bool TryGetFolder(UUID folderId, out InventoryFolder folder, Dictionary<UUID, InventoryFolder> folderCache)
        {
            if(folderCache.TryGetValue(folderId, out folder))
            {
                return true;
            }
            if(m_InventoryService.Folder.TryGetValue(m_Agent.ID, out folder))
            {
                folderCache.Add(folder.ID, folder);
                return true;
            }
            return false;
        }

        public string GetFolderHref(UUID folderId, Dictionary<UUID, InventoryFolder> folderCache)
        {
            InventoryFolder folder;
            if(TryGetFolder(folderId, out folder, folderCache))
            {
                return GetFolderHref(folder, folderCache);
            }
            return m_PrefixUrl + "/category/unknown";
        }

        public string GetFolderHref(InventoryFolder folder, Dictionary<UUID, InventoryFolder> folderCache)
        {
            InventoryFolder parentFolder;
            if(folder.ParentFolderID == UUID.Zero)
            {
                return m_PrefixUrl + "/category/root";
            }
            else if(TryGetFolder(folder.ParentFolderID, out parentFolder, folderCache) && 
                parentFolder.ParentFolderID == UUID.Zero)
            {
                switch(folder.InventoryType)
                {
                    case InventoryType.Animation:
                        return m_PrefixUrl + "/category/animatn";
                    case InventoryType.Bodypart:
                        return m_PrefixUrl + "/category/bodypart";
                    case InventoryType.Clothing:
                        return m_PrefixUrl + "/category/clothing";
                    case InventoryType.CurrentOutfitFolder:
                        return m_PrefixUrl + "/category/current";
                    case InventoryType.FavoriteFolder:
                        return m_PrefixUrl + "/category/favorite";
                    case InventoryType.Gesture:
                        return m_PrefixUrl + "/category/gesture";
                    case InventoryType.Inbox:
                        return m_PrefixUrl + "/category/inbox";
                    case InventoryType.Landmark:
                        return m_PrefixUrl + "/category/landmark";
                    case InventoryType.LSLText:
                        return m_PrefixUrl + "/category/lsltext";
                    case InventoryType.LostAndFoundFolder:
                        return m_PrefixUrl + "/category/lstndfnd";
                    case InventoryType.MyOutfitsFolder:
                        return m_PrefixUrl + "/category/my_otfts";
                    case InventoryType.Notecard:
                        return m_PrefixUrl + "/category/notecard";
                    case InventoryType.Object:
                        return m_PrefixUrl + "/category/object";
                    case InventoryType.Outbox:
                        return m_PrefixUrl + "/category/outbox";
                    case InventoryType.RootFolder:
                        return m_PrefixUrl + "/category/root";
                    case InventoryType.SnapshotFolder:
                        return m_PrefixUrl + "/category/snapshot";
                    case InventoryType.Sound:
                        return m_PrefixUrl + "/category/sound";
                    case InventoryType.Texture:
                        return m_PrefixUrl + "/category/texture";
                    case InventoryType.TrashFolder:
                        return m_PrefixUrl + "/category/trash";
                    default:
                        break;
                }
            }
            return m_PrefixUrl + "/category/" + folder.ID.ToString();
        }

        public void MainHandler(HttpRequest req)
        {
            if(!req.RawUrl.StartsWith(m_PrefixUrl))
            {
                req.ErrorResponse(HttpStatusCode.NotFound, "Not Found");
                return;
            }

            string requrl = req.RawUrl.Substring(m_PrefixUrlLength);

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

        private bool TryFindFolder(string category_id, out InventoryFolder folder, Dictionary<UUID, InventoryFolder> folderCache)
        {
            switch(category_id)
            {
                case "animatn":
                    if(!m_InventoryService.Folder.TryGetValue(m_Agent.ID, AssetType.Animation, out folder))
                    {
                        return false;
                    }
                    break;

                case "bodypart":
                    if (!m_InventoryService.Folder.TryGetValue(m_Agent.ID, AssetType.Bodypart, out folder))
                    {
                        return false;
                    }
                    break;

                case "clothing":
                    if (!m_InventoryService.Folder.TryGetValue(m_Agent.ID, AssetType.Clothing, out folder))
                    {
                        return false;
                    }
                    break;

                case "current":
                    if (!m_InventoryService.Folder.TryGetValue(m_Agent.ID, AssetType.CurrentOutfitFolder, out folder))
                    {
                        return false;
                    }
                    break;

                case "favorite":
                    if (!m_InventoryService.Folder.TryGetValue(m_Agent.ID, AssetType.FavoriteFolder, out folder))
                    {
                        return false;
                    }
                    break;

                case "gesture":
                    if (!m_InventoryService.Folder.TryGetValue(m_Agent.ID, AssetType.Gesture, out folder))
                    {
                        return false;
                    }
                    break;

                case "inbox":
                    if (!m_InventoryService.Folder.TryGetValue(m_Agent.ID, AssetType.Inbox, out folder))
                    {
                        return false;
                    }
                    break;

                case "landmark":
                    if (!m_InventoryService.Folder.TryGetValue(m_Agent.ID, AssetType.Landmark, out folder))
                    {
                        return false;
                    }
                    break;

                case "lsltext":
                    if (!m_InventoryService.Folder.TryGetValue(m_Agent.ID, AssetType.LSLText, out folder))
                    {
                        return false;
                    }
                    break;

                case "lstndfnd":
                    if (!m_InventoryService.Folder.TryGetValue(m_Agent.ID, AssetType.LostAndFoundFolder, out folder))
                    {
                        return false;
                    }
                    break;

                case "my_otfts":
                    if (!m_InventoryService.Folder.TryGetValue(m_Agent.ID, AssetType.MyOutfitsFolder, out folder))
                    {
                        return false;
                    }
                    break;

                case "notecard":
                    if (!m_InventoryService.Folder.TryGetValue(m_Agent.ID, AssetType.Notecard, out folder))
                    {
                        return false;
                    }
                    break;

                case "object":
                    if (!m_InventoryService.Folder.TryGetValue(m_Agent.ID, AssetType.Object, out folder))
                    {
                        return false;
                    }
                    break;

                case "outbox":
                    if (!m_InventoryService.Folder.TryGetValue(m_Agent.ID, AssetType.Outbox, out folder))
                    {
                        return false;
                    }
                    break;

                case "root":
                    if (!m_InventoryService.Folder.TryGetValue(m_Agent.ID, AssetType.RootFolder, out folder))
                    {
                        return false;
                    }
                    break;

                case "snapshot":
                    if (!m_InventoryService.Folder.TryGetValue(m_Agent.ID, AssetType.SnapshotFolder, out folder))
                    {
                        return false;
                    }
                    break;

                case "sound":
                    if (!m_InventoryService.Folder.TryGetValue(m_Agent.ID, AssetType.Sound, out folder))
                    {
                        return false;
                    }
                    break;

                case "texture":
                    if (!m_InventoryService.Folder.TryGetValue(m_Agent.ID, AssetType.Texture, out folder))
                    {
                        return false;
                    }
                    break;

                case "trash":
                    if (!m_InventoryService.Folder.TryGetValue(m_Agent.ID, AssetType.TrashFolder, out folder))
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
                    if(!m_InventoryService.Folder.TryGetValue(m_Agent.ID, id, out folder))
                    {
                        return false;
                    }
                    break;
            }
            folderCache.Add(folder.ID, folder);
            return true;
        }

        private bool TrySplitURL(string rawurl, out string[] elements, out string[] options)
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
        private void ItemHandler(HttpRequest req, string RawUrl)
        {
            string[] elements;
            string[] options;
            if(!TrySplitURL(RawUrl, out elements, out options) || elements.Length != 2)
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }
            switch(req.Method)
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

        private void ItemHandler_Get(HttpRequest req, string[] elements, string[] options)
        {
            UUID itemid;

            if (!UUID.TryParse(elements[1], out itemid))
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }

            InventoryItem item;
            if(!m_InventoryService.Item.TryGetValue(m_Agent.ID, itemid, out item))
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
                    { "_base_uri", m_PrefixUrl + "/item/" + itemid.ToString() }
                };
                var linkref = new Map();
                var href = new Map
                {
                    { "href", m_PrefixUrl + "/item/" + item.AssetID.ToString() }
                };
                linkref.Add("item", href);
                href = new Map
                {
                    { "href", m_PrefixUrl + "/item/" + item.ID.ToString() }
                };
                linkref.Add("self", href);
                href = new Map
                {
                    { "href", GetFolderHref(item.ParentFolderID, folderCache) }
                };
                linkref.Add("parent", href);
                InventoryItem linkeditem;
                if(m_InventoryService.Item.TryGetValue(item.AssetID, out linkeditem))
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
                    { "_base_uri", m_PrefixUrl + "/item/" + itemid.ToString() }
                };
                var linkref = new Map();
                var href = new Map
                {
                    { "href", GetFolderHref(item.AssetID, folderCache) }
                };
                linkref.Add("category", href);
                href = new Map
                {
                    { "href", m_PrefixUrl + "/item/" + item.ID.ToString() }
                };
                linkref.Add("self", href);
                href = new Map
                {
                    { "href", GetFolderHref(item.ParentFolderID, folderCache) }
                };
                linkref.Add("parent", href);
                InventoryFolder linkedfolder;
                if (TryGetFolder(item.AssetID, out linkedfolder, folderCache))
                {
                    var embmap = new Map();
                    resmap.Add("_embedded", embmap);
                    var foldermap = linkedfolder.ToAisV3();
                    embmap.Add("category", foldermap);
                    linkref = new Map();
                    foldermap.Add("_links", linkref);
                    href = new Map
                    {
                        { "href", m_PrefixUrl + "/category/" + linkedfolder.ID.ToString() }
                    };
                    linkref.Add("self", href);
                    href = new Map
                    {
                        { "href", GetFolderHref(linkedfolder.ParentFolderID, folderCache) }
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
                    { "href", m_PrefixUrl + "/item/" + itemid.ToString() }
                };
                linkref.Add("self", href);
                href = new Map
                {
                    { "href", GetFolderHref(item.ParentFolderID, folderCache) }
                };
                linkref.Add("parent", href);
                resmap.Add("_links", linkref);
                resmap.Add("_base_uri", m_PrefixUrl + "/item/" + itemid.ToString());
            }
            SuccessResponse(req, resmap);
        }

        void ItemHandler_Patch(HttpRequest req, string[] elements, string[] options)
        {
            if (req.ContentType != "application/llsd+xml")
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
                using (Stream s = req.Body)
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

        void ItemHandler_Copy(HttpRequest req, string[] elements, string[] options)
        {
            UUID itemid;

            if (!UUID.TryParse(elements[1], out itemid))
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }

            string destinationurl = req["Destination"];
            if (!destinationurl.StartsWith(m_PrefixUrl))
            {
                ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Destination category not found");
                return;
            }
            destinationurl = destinationurl.Substring(m_PrefixUrlLength);
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
                if (!TryFindFolder(destelements[1], out destFolder, folderCache))
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
            if (!m_InventoryService.Item.TryGetValue(m_Agent.ID, itemid, out item))
            {
                ErrorResponse(req, HttpStatusCode.Gone, AisErrorCode.Gone, "Source item gone");
                return;
            }
            item.SetNewID(UUID.Random);
            item.ParentFolderID = destFolder.ID;

            try
            {
                m_InventoryService.Item.Add(item);
            }
            catch
            {
                ErrorResponse(req, HttpStatusCode.Forbidden, AisErrorCode.QueryFailed, "Forbidden");
                return;
            }
            SuccessResponse(req);
        }

        private void ItemHandler_Delete(HttpRequest req, string[] elements, string[] options)
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
                item = m_InventoryService.Item[m_Agent.ID, itemid];
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
                m_InventoryService.Item.Delete(m_Agent.ID, item.ID);
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
        private void FolderHandler(HttpRequest req, string RawUrl)
        {
            string[] elements;
            string[] options;
            if (!TrySplitURL(RawUrl, out elements, out options))
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }

            switch(req.Method)
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

        private void FolderHandler_Get(HttpRequest req, string[] elements, string[] options)
        {
            var folderCache = new Dictionary<UUID, InventoryFolder>();
            InventoryFolder thisFolder;
            if (!TryFindFolder(elements[1], out thisFolder, folderCache))
            {
                ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Not Found");
                return;
            }
            InventoryFolderContent content;
            if(!m_InventoryService.Folder.Content.TryGetValue(m_Agent.ID, thisFolder.ID, out content))
            {
                ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Not Found");
                return;
            }


        }

        private void FolderHandler_Post(HttpRequest req, string[] elements, string[] options)
        {
            if(req.ContentType != "application/llsd+xml")
            {
                ErrorResponse(req, HttpStatusCode.UnsupportedMediaType, AisErrorCode.UnsupportedMedia, "Unsupported media type");
                return;
            }

            Map reqmap;
            try
            {
                using (Stream s = req.Body)
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
                if(!TryFindFolder(elements[1], out folder, folderCache))
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
                items = array.ItemsFromAisV3(m_Agent, folder.ID) :
                new List<InventoryItem>();

            if(reqmap.TryGetValue<AnArray>("links", out array))
            {
                List<InventoryItem> links = array.LinksFromAisV3(m_Agent, folder.ID);
                items.AddRange(from link in links where link.AssetType == AssetType.LinkFolder select link);
                itemlinks.AddRange(from link in links where link.AssetType == AssetType.Link select link);
            }

            if(reqmap.TryGetValue<AnArray>("categories", out array))
            {
                array.CategoriesFromAisV3(m_Agent, folder.ID, folders, items, itemlinks);
            }

            /* linkfolder entries do not need specific handling */
            var linked_ids = new List<UUID>(from link in itemlinks select link.AssetID);
            var dedup_linked_ids = new List<UUID>();
            foreach(UUID id in linked_ids)
            {
                if(!dedup_linked_ids.Contains(id))
                {
                    dedup_linked_ids.Add(id);
                }
            }
            var linkeditems = new Dictionary<UUID, InventoryItem>();
            foreach(InventoryItem item in 
                from linkeditem in m_InventoryService.Item[m_Agent.ID, dedup_linked_ids] select linkeditem)
            {
                linkeditems.Add(item.ID, item);
            }

            foreach(InventoryItem item in itemlinks)
            {
                InventoryItem linkeditem;
                if(linkeditems.TryGetValue(item.AssetID, out linkeditem))
                {
                    item.InventoryType = linkeditem.InventoryType;
                    items.Add(item);
                }
            }

            try
            {
                foreach (InventoryFolder folderentry in folders)
                {
                    m_InventoryService.Folder.Add(folderentry);
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
                    m_InventoryService.Item.Add(item);
                }
            }
            catch
            {
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }

            var resmap = new Map();
            using (var ms = new MemoryStream())
            {
                LlsdXml.Serialize(resmap, ms);
                using (HttpResponse res = req.BeginResponse(HttpStatusCode.Created, "Inventory created", "application/llsd+xml"))
                {
                    using (Stream o = res.GetOutputStream(ms.Length))
                    {
                        o.Write(ms.ToArray(), 0, (int)ms.Length);
                    }
                }
            }
        }

        private void FolderHandler_Put(HttpRequest req, string[] elements, string[] options)
        {
            if (req.ContentType != "application/llsd+xml")
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.UnsupportedMedia, "Unsupported media type");
                return;
            }

            Map reqmap;
            try
            {
                using (Stream s = req.Body)
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

        private void FolderHandler_Patch(HttpRequest req, string[] elements, string[] options)
        {
            if (req.ContentType != "application/llsd+xml")
            {
                ErrorResponse(req, HttpStatusCode.UnsupportedMediaType, AisErrorCode.UnsupportedMedia, "Unsupported media type");
                return;
            }

            Map reqmap;
            try
            {
                using (Stream s = req.Body)
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
                if(!TryFindFolder(elements[1], out folder, folderCache))
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
                m_InventoryService.Folder.Update(folder);
            }
            catch(Exception)
            {
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }
            SuccessResponse(req);
        }

        private void FolderHandler_Delete(HttpRequest req, string[] elements, string[] options)
        {
            InventoryFolder folder;
            var folderCache = new Dictionary<UUID, InventoryFolder>();
            try
            {
                if(!TryFindFolder(elements[1], out folder, folderCache))
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
                    m_InventoryService.Folder.Delete(m_Agent.ID, folder.ID);
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
                    m_InventoryService.Folder.Purge(m_Agent.ID, folder.ID);
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
