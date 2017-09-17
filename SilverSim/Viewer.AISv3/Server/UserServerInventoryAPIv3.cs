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
using SilverSim.Main.Common.HttpServer;
using SilverSim.ServiceInterfaces.Account;
using SilverSim.ServiceInterfaces.Inventory;
using SilverSim.ServiceInterfaces.Presence;
using SilverSim.ServiceInterfaces.Traveling;
using SilverSim.Types;
using SilverSim.Types.Account;
using SilverSim.Types.TravelingData;
using System.ComponentModel;
using System.Net;

namespace SilverSim.Viewer.AISv3.Server
{
    [PluginName("AISv3Handler")]
    [Description("AISv3 user inventory server")]
    public class UserServerInventoryAPIv3 : IPlugin
    {
        private PresenceServiceInterface m_PresenceService;
        private TravelingDataServiceInterface m_TravelingDataService;
        private InventoryServiceInterface m_InventoryService;
        private UserAccountServiceInterface m_UserAccountService;
        private readonly string m_PresenceServiceName;
        private readonly string m_TravelingDataServiceName;
        private readonly string m_InventoryServiceName;
        private readonly string m_UserAccountServiceName;
        private BaseHttpServer m_HttpServer;
        private BaseHttpServer m_HttpsServer;

        /* prefix url is /CAPS/InventoryAPIv3/<agentid>/ */
        public UserServerInventoryAPIv3(IConfig ownSection)
        {
            m_PresenceServiceName = ownSection.GetString("PresenceService");
            m_TravelingDataServiceName = ownSection.GetString("TravelingDataService");
            m_InventoryServiceName = ownSection.GetString("InventoryService");
            m_UserAccountServiceName = ownSection.GetString("UserAccountService");
        }

        public void Startup(ConfigurationLoader loader)
        {
            m_PresenceService = loader.GetService<PresenceServiceInterface>(m_PresenceServiceName);
            m_TravelingDataService = loader.GetService<TravelingDataServiceInterface>(m_TravelingDataServiceName);

            m_InventoryService = loader.GetService<InventoryServiceInterface>(m_InventoryServiceName);
            m_UserAccountService = loader.GetService<UserAccountServiceInterface>(m_UserAccountServiceName);
            m_HttpServer = loader.HttpServer;
            m_HttpServer.StartsWithUriHandlers.Add("/CAPS/InventoryAPIv3/", CapsHandler);
            try
            {
                m_HttpsServer = loader.HttpsServer;
                m_HttpsServer.StartsWithUriHandlers.Add("/CAPS/InventoryAPIv3/", CapsHandler);
            }
            catch
            {
                m_HttpsServer = null;
            }
        }

        private void CapsHandler(HttpRequest req)
        {
            string[] splitquery = req.RawUrl.Split('?');
            string[] elements = splitquery[0].Substring(1).Split('/');

            if(elements.Length < 3)
            {
                req.ErrorResponse(HttpStatusCode.NotFound, "Not found");
                return;
            }

            UUID agentid;
            if(!UUID.TryParse(elements[2], out agentid))
            {
                req.ErrorResponse(HttpStatusCode.NotFound, "Not found");
                return;
            }

            UserAccount account;
            if(!m_UserAccountService.TryGetValue(UUID.Zero, agentid, out account))
            {
                req.ErrorResponse(HttpStatusCode.NotFound, "Not found");
                return;
            }

            bool foundIP = false;
            UUI agent = UUI.Unknown;
            foreach(TravelingDataInfo trv in m_TravelingDataService.GetTravelingDatasByAgentUUID(agentid))
            {
                if(trv.ClientIPAddress == req.CallerIP)
                {
                    foundIP = true;
                }
            }

            if(!foundIP)
            {
                req.ErrorResponse(HttpStatusCode.NotFound, "Not found");
                return;
            }

            string rawPrefixUrl = "/CAPS/InventoryAPIv3/" + agentid;
            string serverURI = req.IsSsl ? m_HttpsServer.ServerURI : m_HttpServer.ServerURI;
            serverURI = serverURI.Substring(0, serverURI.Length - 1);

            AISv3Handler.MainHandler(new AISv3Handler.Request(
                req,
                m_InventoryService,
                account.Principal,
                false,
                rawPrefixUrl,
                serverURI + rawPrefixUrl));
        }
    }
}
