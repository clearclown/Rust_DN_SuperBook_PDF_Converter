using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using DaemonCenter.Models;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Web;
using IPA.Cores.Helper.Web;
using static IPA.Cores.Globals.Web;

using IPA.Cores.Codes;
using IPA.Cores.Helper.Codes;
using static IPA.Cores.Globals.Codes;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;

using IPA.Cores.Basic.App.DaemonCenterLib;

namespace DaemonCenter.Controllers
{
    [AutoValidateAntiforgeryToken]
    [Authorize]
    public class AppSettingsController : Controller
    {
        readonly Server Server;

        public AppSettingsController(Server server)
        {
            this.Server = server;
        }

        [Authorize]
        public IActionResult _new()
        {
            return View();
        }

        public IActionResult Index()
        {
            IReadOnlyList<KeyValuePair<string, App>> appList = Server.AppEnum();

            return View(appList);
        }

        // 追加ページの表示
        [HttpGet]
        public IActionResult Add()
        {
            SingleData<AppSettings> model = new SingleData<AppSettings>();
            model.Mode = ModelMode.Add;

            // デフォルト設定を適用
            Preference pref = Server.GetPreference();
            model.Data.DeadIntervalSecs = pref.DefaultDeadIntervalSecs;
            model.Data.KeepAliveIntervalSecs = pref.DefaultKeepAliveIntervalSecs;
            model.Data.InstanceKeyType = pref.DefaultInstanceKeyType;

            model.Data.Normalize();

            return View("Edit", model);
        }

        // 追加ページのボタンのクリック
        [HttpPost]
        public IActionResult Add([FromForm] SingleData<AppSettings> model)
        {
            model.Mode = ModelMode.Add;

            if (ModelState.IsValid == false)
            {
                return View("Edit", model);
            }

            string appId = Server.AppAdd(model.Data);

            return RedirectToAction(nameof(Index));
        }

        // 編集ページの表示
        [HttpGet]
        public IActionResult Edit([FromRoute] string id)
        {
            AppSettings appSettings = Server.AppGet(id).Settings!;

            SingleData<AppSettings> model = new SingleData<AppSettings>(id, appSettings, ModelMode.Edit);

            return View("Edit", model);
        }

        // 編集ページのボタンのクリック
        [HttpPost]
        public IActionResult Edit([FromRoute] string id, [FromForm] SingleData<AppSettings> model)
        {
            model.Mode = ModelMode.Edit;

            if (ModelState.IsValid == false)
            {
                return View("Edit", model);
            }

            Server.AppSet(id, model.Data);

            return RedirectToAction(nameof(Index));
        }

        // 削除ボタンのクリック
        [HttpGet]
        public IActionResult Delete([FromRoute] string id)
        {
            Server.AppDelete(id);

            return RedirectToAction(nameof(Index));
        }
    }
}
