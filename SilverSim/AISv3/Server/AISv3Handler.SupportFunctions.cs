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

using SilverSim.ServiceInterfaces.Inventory;
using SilverSim.Types;
using SilverSim.Types.Asset;
using SilverSim.Types.Inventory;
using System.Collections.Generic;

namespace SilverSim.AISv3.Server
{
    public static partial class AISv3Handler
    {
        private class AISv3ResultData
        {
            private readonly Map m_Map;
            private readonly Map m_AttachmentsRemoved = new Map();
            private readonly AnArray m_ActiveGesturesRemoved = new AnArray();
            private readonly AnArray m_BrokenLinksRemoved = new AnArray();
            private readonly Map m_WearablesRemoved = new Map();
            private readonly AnArray m_CategoryItemsRemoved = new AnArray();
            private readonly AnArray m_CategoriesRemoved = new AnArray();
            private readonly Map m_UpdatedCategoryVersions = new Map();

            public AISv3ResultData()
            {
                m_Map = new Types.Map
                {
                    ["_attachments_removed"] = m_AttachmentsRemoved,
                    ["_active_gestures_removed"] = m_ActiveGesturesRemoved,
                    ["_broken_links_removed"] = m_BrokenLinksRemoved,
                    ["_wearables_removed"] = m_WearablesRemoved,
                    ["_category_items_removed"] = m_CategoryItemsRemoved,
                    ["_categories_removed"] = m_CategoriesRemoved,
                    ["_updated_category_versions"] = m_UpdatedCategoryVersions
                };
            }

            public void AddRemovedItem(InventoryItem item)
            {
                if (item.AssetType == AssetType.Gesture && (item.Flags & InventoryFlags.GestureActive) != 0)
                {
                    m_ActiveGesturesRemoved.Add(item.ID);
                }
                m_CategoryItemsRemoved.Add(item.ID);
            }

            public void AddUpdatedCategory(InventoryFolder folder)
            {
                m_UpdatedCategoryVersions[folder.ID.ToString()] = new Integer(folder.Version);
            }

            public void AddRemovedCategory(UUID id)
            {
                m_CategoriesRemoved.Add(id);
            }

            public static implicit operator Map(AISv3ResultData data)
            {
                data.m_Map["_total_items_removed"] = new Integer(data.m_CategoryItemsRemoved.Count);
                return data.m_Map;
            }
        }

        private static void PurgeFolder(Request req, UUID id, AISv3ResultData resultData)
        {
            InventoryFolderContent content;
            if(!req.InventoryService.Folder.Content.TryGetValue(req.Agent.ID, id, out content))
            {
                return;
            }

            foreach(InventoryFolder folder in content.Folders)
            {
                DeleteSubFolder(req, folder.ID, resultData);
            }
            foreach(InventoryItem item in content.Items)
            {
                DeleteItem(req, item, resultData);
            }
            InventoryFolder folderData;
            if (req.InventoryService.Folder.TryGetValue(req.Agent.ID, id, out folderData))
            {
                resultData.AddUpdatedCategory(folderData);
            }
        }

        private static void DeleteFolder(Request req, UUID id, AISv3ResultData resultData)
        {
            InventoryFolderContent content;
            InventoryFolder folderData;
            if(!req.InventoryService.Folder.TryGetValue(req.Agent.ID, id, out folderData) ||
                !req.InventoryService.Folder.Content.TryGetValue(req.Agent.ID, id, out content))
            {
                return;
            }

            foreach (InventoryFolder folder in content.Folders)
            {
                DeleteSubFolder(req, folder.ID, resultData);
            }
            foreach (InventoryItem item in content.Items)
            {
                DeleteItem(req, item, resultData);
            }
            try
            {
                req.InventoryService.Folder.Delete(req.Agent.ID, id);
            }
            catch(InventoryFolderNotFoundException)
            {
                /* intentionally ignored */
            }
            if(req.InventoryService.Folder.TryGetValue(req.Agent.ID, folderData.ParentFolderID, out folderData))
            {
                resultData.AddUpdatedCategory(folderData);
            }
        }

        private static void DeleteSubFolder(Request req, UUID id, AISv3ResultData resultData)
        {
            var folders = new List<UUID>();
            int index = 0;
            InventoryFolderContent content;
            while (index++ < folders.Count)
            {
                if (req.InventoryService.Folder.Content.TryGetValue(req.Agent.ID, id, out content))
                {
                    foreach(InventoryItem item in content.Items)
                    {
                        DeleteItem(req, item, resultData);
                    }
                    folders.Add(id);
                    resultData.AddRemovedCategory(id);
                }
            }
            try
            {
                req.InventoryService.Folder.Delete(req.Agent.ID, id);
            }
            catch(InventoryFolderNotFoundException)
            {
                /* ignored */
            }
        }

        private static void DeleteItem(Request req, InventoryItem item, AISv3ResultData resultData)
        {
            try
            {
                req.InventoryService.Item.Delete(req.Agent.ID, item.ID);
                resultData.AddRemovedItem(item);
            }
            catch(InventoryItemNotFoundException)
            {
                /* ignored */
            }
        }
    }
}
