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
using SilverSim.Types;
using SilverSim.Types.Inventory;
using System;
using System.Collections.Generic;
using System.Net;

namespace SilverSim.AISv3.Server
{
    public static partial class AISv3Handler
    {
        private static void FolderChildren_Handler(Request req, string[] elements)
        {
            switch(req.HttpRequest.Method)
            {
                case "GET":
                    FolderHandler_Get(req, elements);
                    break;

                case "DELETE":
                    FolderChildren_DeleteHandler(req, elements);
                    break;

                default:
                    ErrorResponse(req, HttpStatusCode.MethodNotAllowed, AisErrorCode.MethodNotAllowed, "Method not allowed");
                    break;
            }
        }

        private static void FolderChildren_DeleteHandler(Request req, string[] elements)
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

            var result = new AISv3ResultData();
            PurgeFolder(req, folder.ID, result);
            SuccessResponse(req, result);
        }
    }
}
