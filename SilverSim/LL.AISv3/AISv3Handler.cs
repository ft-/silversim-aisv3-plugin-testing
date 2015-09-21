// SilverSim is distributed under the terms of the
// GNU Affero General Public License v3

using SilverSim.Main.Common.HttpServer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace SilverSim.LL.AISv3
{
    class AISv3Handler
    {
        string m_PrefixUrl;
        int m_PrefixUrlLength;
        public AISv3Handler(string prefixurl)
        {
            m_PrefixUrl = prefixurl;
            m_PrefixUrlLength = m_PrefixUrl.Length;
        }

        public void Handler(HttpRequest req)
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

        void ItemHandler(HttpRequest req, string RawUrl)
        {

        }

        void FolderHandler(HttpRequest req, string RawUrl)
        {

        }
    }
}
