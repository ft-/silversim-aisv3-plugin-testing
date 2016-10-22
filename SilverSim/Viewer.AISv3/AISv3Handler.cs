// SilverSim is distributed under the terms of the
// GNU Affero General Public License v3

using SilverSim.Main.Common.HttpServer;
using SilverSim.Types;
using SilverSim.Viewer.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using log4net.Repository;
using SilverSim.Main.Common;
using SilverSim.Types.Inventory;
using SilverSim.ServiceInterfaces.Inventory;
using SilverSim.Types.Asset;
using System.IO;
using SilverSim.Types.StructuredData.Llsd;

namespace SilverSim.Viewer.AISv3
{
    public class AISv3Handler
    {
        string m_PrefixUrl;
        int m_PrefixUrlLength;
        InventoryServiceInterface m_InventoryService;
        UUID m_AgentID;

        enum AisErrorCode
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

        public AISv3Handler(string prefixurl, InventoryServiceInterface inventoryService, UUID agentId)
        {
            m_PrefixUrl = prefixurl;
            m_PrefixUrlLength = m_PrefixUrl.Length;
            m_InventoryService = inventoryService;
            m_AgentID = agentId;
        }

        void SuccessResponse(HttpRequest req, Map m)
        {
            using (MemoryStream ms = new MemoryStream())
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

        void SuccessResponse(HttpRequest req)
        {
            SuccessResponse(req, new Map());
        }

        void ErrorResponse(HttpRequest req, HttpStatusCode code, AisErrorCode errorcode, string description)
        {
            ErrorResponse(req, code, errorcode, description, new Map());
        }

        void ErrorResponse(HttpRequest req, HttpStatusCode code, AisErrorCode errorcode, string description, Map m)
        {
            m.Add("error_code", (int)errorcode);
            using (MemoryStream ms = new MemoryStream())
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
                ItemHandler(req, requrl);
            }
            else if(requrl.StartsWith("/category/"))
            {
                FolderHandler(req, requrl);
            }
            else
            {
                req.ErrorResponse(HttpStatusCode.NotFound, "Not Found");
            }
        }

        InventoryFolder FindFolder(HttpRequest req, string category_id)
        {
            switch(category_id)
            {
                case "animatn":
                    return m_InventoryService.Folder[m_AgentID, AssetType.Animation];

                case "bodypart":
                    return m_InventoryService.Folder[m_AgentID, AssetType.Bodypart];

                case "clothing":
                    return m_InventoryService.Folder[m_AgentID, AssetType.Clothing];

                case "current":
                    return m_InventoryService.Folder[m_AgentID, AssetType.CurrentOutfitFolder];

                case "favorite":
                    return m_InventoryService.Folder[m_AgentID, AssetType.FavoriteFolder];

                case "gesture":
                    return m_InventoryService.Folder[m_AgentID, AssetType.Gesture];

                case "inbox":
                    return m_InventoryService.Folder[m_AgentID, AssetType.Inbox];

                case "landmark":
                    return m_InventoryService.Folder[m_AgentID, AssetType.Landmark];

                case "lsltext":
                    return m_InventoryService.Folder[m_AgentID, AssetType.LSLText];

                case "lstndfnd":
                    return m_InventoryService.Folder[m_AgentID, AssetType.LostAndFoundFolder];

                case "my_otfts":
                    return m_InventoryService.Folder[m_AgentID, AssetType.OutfitFolder];

                case "notecard":
                    return m_InventoryService.Folder[m_AgentID, AssetType.Notecard];

                case "object":
                    return m_InventoryService.Folder[m_AgentID, AssetType.Object];

                case "outbox":
                    return m_InventoryService.Folder[m_AgentID, AssetType.Outbox];

                case "root":
                    return m_InventoryService.Folder[m_AgentID, AssetType.RootFolder];

                case "snapshot":
                    return m_InventoryService.Folder[m_AgentID, AssetType.SnapshotFolder];

                case "sound":
                    return m_InventoryService.Folder[m_AgentID, AssetType.Sound];

                case "texture":
                    return m_InventoryService.Folder[m_AgentID, AssetType.Texture];

                case "trash":
                    return m_InventoryService.Folder[m_AgentID, AssetType.TrashFolder];

                default:
                    UUID id;
                    if(!UUID.TryParse(category_id, out id))
                    {
                        throw new KeyNotFoundException("Invalid category id");
                    }
                    return m_InventoryService.Folder[m_AgentID, id];
            }
        }

        bool TrySplitURL(string rawurl, out string[] elements, out string[] options)
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
        void ItemHandler(HttpRequest req, string RawUrl)
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

        void ItemHandler_Get(HttpRequest req, string[] elements, string[] options)
        {
            UUID itemid;

            if (!UUID.TryParse(elements[1], out itemid))
            {
                ErrorResponse(req, HttpStatusCode.BadRequest, AisErrorCode.InvalidRequest, "Bad request");
                return;
            }

            InventoryItem item;
            if(!m_InventoryService.Item.TryGetValue(m_AgentID, itemid, out item))
            {
                ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Not Found");
                return;
            }

            Map resmap;
            if(item.AssetType == AssetType.Link)
            {
                resmap = new Map();
            }
            else if(item.AssetType == AssetType.LinkFolder)
            {
                resmap = new Map();
            }
            else
            {
                resmap = item.ToAisV3();
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
            try
            {
                destFolder = FindFolder(req, destelements[1]);
            }
            catch (KeyNotFoundException)
            {
                ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Destination category not found");
                return;
            }
            catch
            {
                ErrorResponse(req, HttpStatusCode.InternalServerError, AisErrorCode.InternalError, "Internal Server Error");
                return;
            }

            InventoryItem item;
            if (!m_InventoryService.Item.TryGetValue(m_AgentID, itemid, out item))
            {
                ErrorResponse(req, HttpStatusCode.Gone, AisErrorCode.Gone, "Source item gone");
                return;
            }
            item.ID = UUID.Random;
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

        void ItemHandler_Delete(HttpRequest req, string[] elements, string[] options)
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
                item = m_InventoryService.Item[m_AgentID, itemid];
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
                m_InventoryService.Item.Delete(m_AgentID, item.ID);
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
        void FolderHandler(HttpRequest req, string RawUrl)
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
                    FolderHandler_Get(req, elements, options);
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

        void FolderHandler_Get(HttpRequest req, string[] elements, string[] options)
        {

        }

        void FolderHandler_Post(HttpRequest req, string[] elements, string[] options)
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
        }

        void FolderHandler_Put(HttpRequest req, string[] elements, string[] options)
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

        void FolderHandler_Patch(HttpRequest req, string[] elements, string[] options)
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
            try
            {
                folder = FindFolder(req, elements[1]);
            }
            catch (KeyNotFoundException)
            {
                ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Not Found");
                return;
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

        void FolderHandler_Delete(HttpRequest req, string[] elements, string[] options)
        {
            InventoryFolder folder;
            try
            {
                folder = FindFolder(req, elements[1]);
            }
            catch (KeyNotFoundException)
            {
                ErrorResponse(req, HttpStatusCode.NotFound, AisErrorCode.NotFound, "Not Found");
                return;
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
                    m_InventoryService.Folder.Delete(m_AgentID, folder.ID);
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
                    m_InventoryService.Folder.Purge(m_AgentID, folder.ID);
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
