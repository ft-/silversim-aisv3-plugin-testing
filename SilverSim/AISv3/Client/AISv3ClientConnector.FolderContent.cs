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

using SilverSim.Http.Client;
using SilverSim.ServiceInterfaces.Inventory;
using SilverSim.Types;
using SilverSim.Types.Inventory;
using SilverSim.Types.StructuredData.Llsd;
using System.Collections.Generic;
using System.IO;
using System.Web;

namespace SilverSim.AISv3.Client
{
    public sealed partial class AISv3ClientConnector : IInventoryFolderContentServiceInterface
    {
        InventoryFolderContent IInventoryFolderContentServiceInterface.this[UUID principalID, UUID folderID]
        {
            get
            {
                InventoryFolderContent content;
                if(!Folder.Content.TryGetValue(principalID, folderID, out content))
                {
                    throw new InventoryFolderNotFoundException(folderID);
                }
                return content;
            }
        }

        List<InventoryFolderContent> IInventoryFolderContentServiceInterface.this[UUID principalID, UUID[] folderIDs]
        {
            get
            {
                var result = new List<InventoryFolderContent>();
                foreach(UUID id in folderIDs)
                {
                    InventoryFolderContent content;
                    if(Folder.Content.TryGetValue(principalID, id, out content))
                    {
                        result.Add(content);
                    }
                }
                return result;
            }
        }

        bool IInventoryFolderContentServiceInterface.ContainsKey(UUID principalID, UUID folderID)
        {
            return Folder.ContainsKey(principalID, folderID);
        }

        bool IInventoryFolderContentServiceInterface.TryGetValue(UUID principalID, UUID folderID, out InventoryFolderContent inventoryFolderContent)
        {
            IValue iv;
            try
            {
                using (Stream s = new HttpClient.Get($"{m_CapabilityUri}category/{folderID}?depth=1")
                {
                    TimeoutMs = TimeoutMs
                }.ExecuteStreamRequest())
                {
                    iv = LlsdXml.Deserialize(s);
                }
            }
            catch (HttpException e)
            {
                if (e.GetHttpCode() == 404)
                {
                    inventoryFolderContent = default(InventoryFolderContent);
                    return false;
                }
                throw;
            }

            var resmap = iv as Map;
            if (resmap == null)
            {
                throw new InvalidDataException();
            }

            inventoryFolderContent = new InventoryFolderContent();
            inventoryFolderContent.FolderID = resmap["category_id"].AsUUID;
            inventoryFolderContent.Owner = new UGUI(resmap["agent_id"].AsUUID);
            inventoryFolderContent.Folders = ExtractFolders(resmap);
            inventoryFolderContent.Items = ExtractItems(resmap);

            return true;
        }
    }
}
