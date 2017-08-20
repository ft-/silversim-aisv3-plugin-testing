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

using SilverSim.Main.Common;
using SilverSim.Main.Common.HttpServer;
using SilverSim.Viewer.Core;
using System.Net;

namespace SilverSim.Viewer.AISv3
{
    [PluginName("SimInventoryAPIv3")]
    public sealed class SimInventoryAPIv3 : ICapabilityExtender, IPlugin
    {
        private const string PrefixCapsUrl = "/CAPS/InventoryAPIv3/00000000-0000-0000-0000-000000000000";
        private BaseHttpServer m_HttpServer;
        private BaseHttpServer m_HttpsServer;

        public void Startup(ConfigurationLoader loader)
        {
            m_HttpServer = loader.HttpServer;
            try
            {
                m_HttpsServer = loader.HttpsServer;
            }
            catch
            {
                m_HttpsServer = null;
            }
        }

        [CapabilityHandler("InventoryAPIv3")]
        public void HandleInventoryAPIv3(ViewerAgent agent, AgentCircuit circuit, HttpRequest req)
        {
            string capsUrl = req.IsSsl ? m_HttpsServer.ServerURI : m_HttpServer.ServerURI;
            string rawPrefixUrl = req.RawUrl.Substring(0, PrefixCapsUrl.Length);
            capsUrl += rawPrefixUrl.Substring(1);

            if (req.CallerIP != circuit.RemoteIP)
            {
                req.ErrorResponse(HttpStatusCode.Forbidden, "Forbidden");
                return;
            }

            var reqcontext = new AISv3Handler.Request(req, agent.InventoryService, agent.Owner, false, rawPrefixUrl, capsUrl);
            AISv3Handler.MainHandler(reqcontext);
        }
    }
}