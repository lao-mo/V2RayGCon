﻿using Pacman.Resources.Langs;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Pacman.Controllers
{
    class FormMainCtrl
    {
        Services.Settings settings;

        Button btnSave, btnDelete, btnPull, btnImport;
        TextBox tboxName;
        FlowLayoutPanel flyContent;
        ListBox lstBoxPackages;
        List<Models.Data.Bean> beanList;

        public FormMainCtrl(
            Services.Settings settings,

            TextBox tboxName,
            FlowLayoutPanel flyContent,
            ListBox lstBoxPackages,
            Button btnSave,
            Button btnDelete,
            Button btnPull,
            Button btnImport,

            Button btnSelectAll,
            Button btnSelectInvert,
            Button btnSelectNone,
            Button btnDeleteSelected,
            Button btnRefreshSelected)
        {
            this.settings = settings;

            BindControls(
                tboxName,
                flyContent,
                lstBoxPackages,
                btnSave,
                btnDelete,
                btnPull,
                btnImport);

            BindEvent(
                btnSelectAll,
                btnSelectInvert,
                btnSelectNone,
                btnDeleteSelected,
                btnRefreshSelected);

        }

        #region drag and drop
        List<string> dropableObjectsTypeString = new List<string> {
            "Pacman.Views.UserControls.BeanUI",
            "V2RayGCon.Views.UserControls.ServerUI",
        };

        bool IsDropableObject(DragEventArgs args)
        {
            foreach (string type in args.Data.GetFormats())
            {
                if (dropableObjectsTypeString.Contains(type))
                {
                    return true;
                }
            }
            return false;
        }

        void ResetFlyContentItemsIndex()
        {
            var controlList = flyContent.Controls;
            var count = 1;
            foreach (Views.UserControls.BeanUI control in controlList)
            {
                control.index = count++;
            }
        }

        void BindDragDropEvent()
        {
            flyContent.DragEnter += (s, a) =>
            {
                if (!IsDropableObject(a))
                {
                    return;
                }
                a.Effect = DragDropEffects.All;
            };

            flyContent.DragDrop += (s, a) =>
            {
                Views.UserControls.BeanUI beanUI = null;
                // https://www.codeproject.com/Articles/48411/Using-the-FlowLayoutPanel-and-Reordering-with-Drag
                if (a.Data.GetDataPresent("V2RayGCon.Views.UserControls.ServerUI"))
                {
                    var item = (VgcApis.Models.IDropableControl)a.Data.GetData("V2RayGCon.Views.UserControls.ServerUI");
                    var bean = new Models.Data.Bean
                    {
                        title = item.GetTitle(),
                        uid = item.GetUid(),
                    };

                    foreach (Views.UserControls.BeanUI control in flyContent.Controls)
                    {
                        if (control.GetBean().uid == bean.uid)
                        {
                            // update title
                            control.SetTitle(bean.title);
                            return;
                        }
                    }

                    beanUI = new Views.UserControls.BeanUI(bean);
                    flyContent.Controls.Add(beanUI);
                }

                if (beanUI == null && a.Data.GetDataPresent(typeof(Views.UserControls.BeanUI)))
                {
                    beanUI = (Views.UserControls.BeanUI)
                        a.Data.GetData(typeof(Views.UserControls.BeanUI));
                }

                if (beanUI == null)
                {
                    return;
                }

                var destIndex = flyContent.Controls.GetChildIndex(
                    flyContent.GetChildAtPoint(
                        flyContent.PointToClient(
                            new Point(a.X, a.Y))),
                    false);
                flyContent.Controls.SetChildIndex(beanUI, destIndex);

                ResetFlyContentItemsIndex();
            };

        }

        #endregion

        #region public methods
        public void Run()
        {
            RefreshPackageListBox();
            RefreshFlyContent();
            BindControlsEvent();
            BindDragDropEvent();
        }

        private void BindControlsEvent()
        {
            btnSave.Click += (s, a) => SaveCurPackageSetting();
            btnDelete.Click += (s, a) => DeletePackage();
            lstBoxPackages.SelectedIndexChanged += (s, a) => PackageListSelectedIndexChanged();
            btnPull.Click += (s, a) => PullSelectedServerFromMainWindow();
            btnImport.Click += (s, a) => ImportPackage();
        }
        #endregion

        #region private methods 
        private void BindEvent(
          Button btnSelectAll,
          Button btnSelectInvert,
          Button btnSelectNone,
          Button btnDeleteSelected,
          Button btnRefreshSelected)
        {
            btnSelectAll.Click +=
                (s, a) => LoopThroughFlyContentItems(
                    (b) => b.Select(true));
            btnSelectNone.Click +=
                (s, a) => LoopThroughFlyContentItems(
                    (b) => b.Select(false));
            btnSelectInvert.Click +=
                (s, a) => LoopThroughFlyContentItems(
                    (b) => b.InvertSelection());
            btnDeleteSelected.Click +=
                (s, a) => LoopThroughFlyContentItems(
                    (b) =>
                    {
                        if (b.isSelected)
                        {
                            flyContent.Controls.Remove(b);
                        }
                    });
            btnRefreshSelected.Click += (s, a) =>
            {
                var list = settings.GetAllServersList();
                LoopThroughFlyContentItems(b =>
                {
                    var bean = b.GetBean();
                    var c = list.FirstOrDefault(t => t.GetUid() == bean.uid);
                    if (c == null)
                    {
                        flyContent.Controls.Remove(b);
                        return;
                    }
                    b.SetTitle(c.GetTitle());
                });

            };
        }

        void LoopThroughFlyContentItems(Action<Views.UserControls.BeanUI> action)
        {
            var controls = flyContent.Controls.OfType<Views.UserControls.BeanUI>().ToList();

            foreach (Views.UserControls.BeanUI control in controls)
            {
                action(control);
            }
        }

        void ImportPackage()
        {
            var uidList = GetFlyContentBeanList()
                .Where(b => b.isSelected)
                .Select(b => b.uid)
                .ToList();

            var list = settings
                .GetAllServersList()
                .Where(s => uidList.Contains(s.GetUid()))
                .ToList();

            settings.ImportPackage(list);
        }

        List<Models.Data.Bean> GetFlyContentBeanList()
        {
            var result = new List<Models.Data.Bean>();
            foreach (Views.UserControls.BeanUI beanUI in flyContent.Controls)
            {
                result.Add(beanUI.GetBean().Clone());
            }
            return result;
        }

        void PullSelectedServerFromMainWindow()
        {
            var curList = GetFlyContentBeanList();
            var selectedServerList = settings
                .GetAllServersList()
                .Where(s => s.IsSelected())
                .ToList();

            foreach (var serverCtrl in selectedServerList)
            {
                var found = curList.FirstOrDefault(b => b.uid == serverCtrl.GetUid());
                if (found != null)
                {
                    found.title = serverCtrl.GetTitle();
                    continue;
                }
                curList.Add(new Models.Data.Bean
                {
                    title = serverCtrl.GetTitle(),
                    uid = serverCtrl.GetUid(),
                });
            }
            this.beanList = curList;
            RefreshFlyContent();
        }

        void RefreshFlyContent()
        {
            flyContent.Controls.Clear();
            if (beanList == null)
            {
                return;
            }

            var sortedList = beanList
                .OrderBy(b => b.index)
                .ToList();

            foreach (var bean in sortedList)
            {
                flyContent.Controls.Add(
                    new Views.UserControls.BeanUI(bean));
            }
        }

        void PackageListSelectedIndexChanged()
        {
            var index = lstBoxPackages.SelectedIndex;
            var package = settings.GetPackageByIndex(index);
            ShowPackage(package);
        }

        void ShowPackage(Models.Data.Package package)
        {
            tboxName.Text = package.name;
            beanList = package.beans.Select(b => b.Clone()).ToList();
            RefreshFlyContent();
        }

        void DeletePackage()
        {
            var name = tboxName.Text;

            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            if (!VgcApis.Utils.Confirm(string.Format(I18N.DeleteTpl, name)))
            {
                return;
            }

            settings.RemovePackageByName(name);
            RefreshPackageListBox();
        }

        void SaveCurPackageSetting()
        {
            var name = tboxName.Text;
            if (string.IsNullOrEmpty(name))
            {
                Libs.UI.MsgBoxAsync(I18N.NameCanNotBeNull);
                return;
            }
            var package = new Models.Data.Package();
            package.name = name;
            package.beans = GetFlyContentBeanList();
            settings.SavePackage(package);
            RefreshPackageListBox();
        }

        private void RefreshPackageListBox()
        {
            var packages = settings.GetPackageList();
            this.lstBoxPackages.Items.Clear();
            foreach (var package in packages)
            {
                lstBoxPackages.Items.Add(package.name);
            }
        }
        #endregion

        #region UI 
        private void BindControls(TextBox tboxName, FlowLayoutPanel flyContent, ListBox lstBoxPackages, Button btnSave, Button btnDelete, Button btnPull, Button btnGenerate)
        {
            this.tboxName = tboxName;
            this.flyContent = flyContent;
            this.lstBoxPackages = lstBoxPackages;
            this.btnDelete = btnDelete;
            this.btnImport = btnGenerate;
            this.btnPull = btnPull;
            this.btnSave = btnSave;
        }
        #endregion
    }
}
