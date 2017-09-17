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
using System;
using System.Collections.Generic;
using System.Net;

namespace SilverSim.Viewer.AISv3.Server
{
    public static partial class AISv3Handler
    {
        private static void FolderFolders_Handler(Request req, string[] elements)
        {
            switch (req.HttpRequest.Method)
            {
                case "GET":
                    FolderFolders_Get(req, elements);
                    break;

                case "DELETE":
                    FolderFolders_DeleteHandler(req, elements);
                    break;

                default:
                    ErrorResponse(req, HttpStatusCode.MethodNotAllowed, AisErrorCode.MethodNotAllowed, "Method not allowed");
                    break;
            }
        }

        private static void FolderFolders_Get(Request req, string[] elements)
        {
            var folderCache = new Dictionary<UUID, InventoryFolder>();
            InventoryFolder thisFolder;
            if (!TryFindFolder(req, elements[4], out thisFolder, folderCache))
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

                    var embeddedcategories = new Map();
                    var embedded = new Map
                    {
                        ["categories"] = embeddedcategories,
                    };
                    foreach (InventoryFolder folder in e.Content.Folders)
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

        private static void FolderFolders_DeleteHandler(Request req, string[] elements)
        {
            InventoryFolder folder;
            var folderCache = new Dictionary<UUID, InventoryFolder>();
            try
            {
                if (!TryFindFolder(req, elements[4], out folder, folderCache))
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

            List<InventoryFolder> folders = req.InventoryService.Folder.GetFolders(req.Agent.ID, folder.ID);
            var result = new AISv3ResultData();
            foreach (InventoryFolder subfolder in folders)
            {
                DeleteFolder(req, subfolder.ID, result);
            }
            SuccessResponse(req, result);
        }
    }
}
