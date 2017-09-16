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

using Nini.Config;
using SilverSim.Main.Common;
using SilverSim.ServiceInterfaces.Inventory;
using SilverSim.Types;
using SilverSim.Types.Inventory;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SilverSim.Viewer.AISv3.Client
{
    [PluginName("AISv3Client")]
    [Description("AISv3 client connector")]
    public sealed partial class AISv3ClientConnector : InventoryServiceInterface, IPlugin
    {
        private readonly string m_CapabilityUri;
        public int TimeoutMs { get; set; }

        public AISv3ClientConnector(IConfig ownSection)
        {
            TimeoutMs = 20000;
            m_CapabilityUri = ownSection.GetString("Uri");
            if(!m_CapabilityUri.EndsWith("/"))
            {
                m_CapabilityUri += "/";
            }
        }

        public AISv3ClientConnector(string url)
        {
            TimeoutMs = 20000;
            m_CapabilityUri = url;
            if (!m_CapabilityUri.EndsWith("/"))
            {
                m_CapabilityUri += "/";
            }
        }

        public void Startup(ConfigurationLoader loader)
        {
            /* intentionally left empty */
        }

        public override IInventoryFolderServiceInterface Folder => this;

        public override IInventoryItemServiceInterface Item => this;

        public override List<InventoryItem> GetActiveGestures(UUID principalID)
        {
            throw new NotSupportedException();
        }

        public override void Remove(UUID scopeID, UUID accountID)
        {
            throw new NotSupportedException();
        }
    }
}
