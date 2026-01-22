using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Web;
using IPA.Cores.Helper.Web;
using static IPA.Cores.Globals.Web;

namespace IPA.Cores.Web;

[AspNetLibFeature(AspNetLibFeatures.Any)]
public class EmbedTestController : Controller
{
    public IActionResult Index()
    {
        Dbg.Where();
        return View();
    }

    public IActionResult Test()
    {
        Dbg.Where();
        ViewBag.Test = DateTime.Now._ToDtStr(withMSecs: true);
        return View();
    }
}
