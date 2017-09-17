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

            if (reqmap.TryGetValue<AnArray>("links", out array))
            {
                List<InventoryItem> links = array.LinksFromAisV3(req.Agent, folder.ID);
                items.AddRange(from link in links where link.AssetType == AssetType.LinkFolder select link);
                itemlinks.AddRange(from link in links where link.AssetType == AssetType.Link select link);
            }

            if (reqmap.TryGetValue<AnArray>("categories", out array))
            {
                array.CategoriesFromAisV3(req.Agent, folder.ID, folders, items, itemlinks);
            }

            /* linkfolder entries do not need specific handling */
            var linked_ids = new List<UUID>(from link in itemlinks select link.AssetID);
            var dedup_linked_ids = new List<UUID>();
            var rescreateditems = new AnArray();
            foreach (UUID id in linked_ids)
            {
                if (!dedup_linked_ids.Contains(id))
                {
                    dedup_linked_ids.Add(id);
                }
            }
            var linkeditems = new Dictionary<UUID, InventoryItem>();
            foreach (InventoryItem item in
                from linkeditem in req.InventoryService.Item[req.Agent.ID, dedup_linked_ids] select linkeditem)
            {
                linkeditems.Add(item.ID, item);
                rescreateditems.Add(item.ID);
            }

            foreach (InventoryItem item in itemlinks)
            {
                InventoryItem linkeditem;
                if (linkeditems.TryGetValue(item.AssetID, out linkeditem))
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
                foreach (InventoryItem item in items)
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
            foreach (InventoryFolder folderentry in folders)
            {
                resarray.Add(folderentry.ID);
            }
            resmap.Add("_created_categories", resarray);
            resmap.Add("_created_items", rescreateditems);
            SuccessResponse(req, resmap);
        }
    }
}
