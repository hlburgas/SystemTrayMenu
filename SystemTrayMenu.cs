﻿using Clearcove.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SystemTrayMenu.Controls;
using SystemTrayMenu.Handler;
using SystemTrayMenu.Helper;

namespace SystemTrayMenu
{
    class SystemTrayMenu : IDisposable
    {
        MessageFilter messageFilter = new MessageFilter();
        bool messageFilterAdded = false;

        MenuNotifyIcon menuNotifyIcon = null;
        WaitFastLeave fastLeave = new WaitFastLeave();
        Menu[] menus = new Menu[MenuDefines.MenusMax];
        KeyboardInput keyboardInput;

        enum OpenCloseState { Default, Opening, Closing };
        OpenCloseState openCloseState = OpenCloseState.Default;

        BackgroundWorker worker = new BackgroundWorker();
        Screen screen = Screen.PrimaryScreen;

        DataGridView dgvFromLastMouseEvent = null;
        DataGridViewCellEventArgs cellEventArgsFromLastMouseEvent = null;

        public SystemTrayMenu()
        {
            keyboardInput = new KeyboardInput(menus);
            keyboardInput.RegisterHotKey();
            keyboardInput.HotKeyPressed += SwitchOpenClose;
            keyboardInput.ClosePressed += MenusFadeOut;
            keyboardInput.RowSelected += KeyboardInputRowSelected;
            void KeyboardInputRowSelected(DataGridView dgv, int rowIndex)
            {
                keyboardInput.InUse = true;
                FadeInIfNeeded();
                CheckMenuOpenerStart(dgv, rowIndex);
            }
            keyboardInput.RowDeselected += CheckMenuOpenerStop;
            keyboardInput.Cleared += FadeHalfOrOutIfNeeded;

            menuNotifyIcon = new MenuNotifyIcon();

            menus[0] = new Menu();
            menus[0].Dispose();
            menuNotifyIcon.Exit += Application.Exit;

            menuNotifyIcon.HandleClick += SwitchOpenClose;
            void SwitchOpenClose()
            {
                if (Config.Path == string.Empty)
                {
                    //Case when Folder Dialog open
                }
                else if (openCloseState == OpenCloseState.Opening ||
                    menus[0].Visible && openCloseState == OpenCloseState.Default)
                {
                    openCloseState = OpenCloseState.Closing;
                    MenusFadeOut();
                    if (worker.IsBusy)
                    {
                        worker.CancelAsync();
                    }
                    if (!Menus().Any(m => m.Visible))
                    {
                        openCloseState = OpenCloseState.Default;
                    }
                }
                else
                {
                    #region NotifyIconIsInteededToBeOutside
                    // Idea is either to show always outside like dropbox 
                    // (is nok due to windows rules, maybe only allowed when asking user?)
                    // (or to give at least a hint that drag drop possible?)
                    //bool IsNotifyIconInTaskbar()
                    //{
                    //    bool isNotifyIconInTaskbar = false;
                    //    int height = screen.Bounds.Height -
                    //        new Taskbar().Size.Height;
                    //    if (Cursor.Position.Y >= height)
                    //    {
                    //        isNotifyIconInTaskbar = true;
                    //    }
                    //    return isNotifyIconInTaskbar;
                    //}
                    //if (!IsNotifyIconInTaskbar())
                    //{
                    //    //DragDropHintForm hintForm = new DragDropHintForm(
                    //    //    Language.Translate("HintDragDropTitle"),
                    //    //    Language.Translate("HintDragDropText"),
                    //    //    Language.Translate("buttonOk"));
                    //    //hintForm.Show();
                    //}
                    #endregion

                    openCloseState = OpenCloseState.Opening;
                    menuNotifyIcon.LoadingStart();
                    worker.RunWorkerAsync();
                }
            }

            worker.WorkerSupportsCancellation = true;
            worker.DoWork += Worker_DoWork;
            void Worker_DoWork(object senderDoWork,
                DoWorkEventArgs eDoWork)
            {
                int level = 0;
                BackgroundWorker worker = (BackgroundWorker)senderDoWork;
                eDoWork.Result = ReadMenu(worker, Config.Path, level);
            }

            worker.RunWorkerCompleted += Worker_RunWorkerCompleted;
            void Worker_RunWorkerCompleted(object sender,
                RunWorkerCompletedEventArgs e)
            {
                keyboardInput.ResetSelectedByKey();
                menuNotifyIcon.LoadingStop();
                MenuData menuData = (MenuData)e.Result;
                if (menuData.Validity == MenuDataValidity.Valid)
                {
                    DisposeMenu(menus[0]);
                    menus[0] = CreateMenu(menuData, Path.GetFileName(Config.Path));
                    menus[0].AdjustLocationAndSize(screen);
                    ActivateMenu();
                    menus[0].AdjustLocationAndSize(screen);
                    if (!messageFilterAdded)
                    {
                        Application.AddMessageFilter(messageFilter);
                        messageFilterAdded = true;
                    }
                }

                openCloseState = OpenCloseState.Default;
            }

            void ActivateMenu()
            {
                Menus().ToList().ForEach(menu =>
                {
                    menu.FadeIn();
                    menu.FadeHalf();
                });
                menus[0].SetTitleColorActive();
                menus[0].Activate();
                WindowToTop.ForceForegroundWindow(menus[0].Handle);
            }

            menuNotifyIcon.OpenLog += Log.OpenLogFile;
            menuNotifyIcon.ChangeFolder += ChangeFolder;
            void ChangeFolder()
            {
                if (Config.SetFolderByUser())
                {
                    ApplicationRestart();
                }
            }

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
            {
                Log.Info("SystemEvents_DisplaySettingsChanged");
                ApplicationRestart();
            }

            menuNotifyIcon.Restart += ApplicationRestart;
            void ApplicationRestart()
            {
                Dispose();
                Program.Restart();
            }

            messageFilter.MouseMove += FadeInIfNeeded;
            messageFilter.MouseMove += MessageFilter_MouseMove;
            void MessageFilter_MouseMove()
            {
                if (keyboardInput.InUse)
                {
                    CheckMenuOpenerStop(keyboardInput.iMenuKey,
                        keyboardInput.iRowKey);
                    keyboardInput.ClearIsSelectedByKey();

                    keyboardInput.InUse = false;
                    if (dgvFromLastMouseEvent != null)
                    {
                        Dgv_MouseEnter(dgvFromLastMouseEvent, 
                            cellEventArgsFromLastMouseEvent);
                    }
                }
            }
            messageFilter.ScrollBarMouseMove += FadeInIfNeeded;

            messageFilter.MouseLeave += fastLeave.Start;
            fastLeave.Leave += FadeHalfOrOutIfNeeded;
        }

        void FadeInIfNeeded()
        {
            if (menus[0].Visible &&
                !menus[0].IsFadingIn &&
                !menus[0].IsFadingOut)
            {
                if (Menus().Any(m => m.Opacity < 1))
                {
                    Menus().ToList().ForEach(menu => menu.FadeIn());
                }
            }
        }

        void FadeHalfOrOutIfNeeded()
        {
            Point mousePosition = Control.MousePosition;
            bool isMouseOnAnyMenu =
                !Menus().Any(m => m.IsMouseOn(mousePosition));
            bool isAnyMenuActive = IsAnyMenuActive();

            if (menus[0].Visible &&
                isMouseOnAnyMenu)
            {
                if (isAnyMenuActive && 
                    !(openCloseState == OpenCloseState.Closing))
                {
                    if (!keyboardInput.InUse)
                    {
                        Menus().ToList().ForEach(menu => menu.FadeHalf());
                    }
                }
                else
                {
                    MenusFadeOut();
                }
            }
        }

        public void Dispose()
        {
            keyboardInput.Dispose();
            menuNotifyIcon.Dispose();
            fastLeave.Dispose();
            DisposeMenu(menus[0]);
        }

        void DisposeMenu(Menu menuToDispose)
        {
            if (menuToDispose != null)
            {
                DataGridView dgv = menuToDispose.GetDataGridView();
                foreach (DataGridViewRow row in dgv.Rows)
                {
                    RowData rowData = (RowData)row.Tag;
                    rowData.Dispose();
                    DisposeMenu(rowData.SubMenu);
                }
                dgv.ClearSelection();
                menuToDispose.Dispose();
            }
        }

        void DisposeWhenHidden(object sender, EventArgs e)
        {
            Menu menuToDispose = (Menu)sender;
            if (!menuToDispose.Visible)
            {
                DisposeMenu(menuToDispose);
            }
            if (Menus().Any(m => m.IsDisposed))
            {
                openCloseState = OpenCloseState.Default;
            }
        }

        void AdjustSubMenusLocationAndSize()
        {
            int heightMax = screen.Bounds.Height -
                new Taskbar().Size.Height;
            Menu menuPredecessor = menus[0];
            int widthPredecessors = -1; // -1 padding
            bool directionToRight = false;

            foreach (Menu menu in Menus().Skip(1))
            {
                // -1*2 padding
                int newWith = (menu.Width - 2 + menuPredecessor.Width);
                if (directionToRight)
                {
#warning CodeBuity&Refactor #49 is this still correct?
                    if (widthPredecessors - menu.Width <= -2) // -1*2 padding
                    {
                        directionToRight = false;
                    }
                    else
                    {
                        widthPredecessors -= newWith;
                    }
                }
                else if (screen.Bounds.Width <
                    widthPredecessors + menuPredecessor.Width + menu.Width)
                {
                    directionToRight = true;
                    widthPredecessors -= newWith;
                }

                menu.AdjustLocationAndSize(heightMax,
                    widthPredecessors, menuPredecessor);
                widthPredecessors += menu.Width - 1; // -1 padding
                menuPredecessor = menu;
            }
        }

        void OpenSubMenu(object sender, EventArgs e)
        {
            RowData trigger = (RowData)sender;
            Menu menuTriggered = trigger.SubMenu;
            Menu menuFromTrigger = menus[menuTriggered.Level - 1];

            for (int level = menuTriggered.Level;
                level < MenuDefines.MenusMax; level++)
            {
                if (menus[level] != null)
                {
                    Menu menuToClose = menus[level];
                    RowData oldTrigger = (RowData)menuToClose.Tag;
                    DataGridView dgv = menuFromTrigger.GetDataGridView();
                    foreach (DataGridViewRow row in dgv.Rows)
                    {
                        RowData rowData = (RowData)row.Tag;
                        rowData.IsSelected = false;
                    }
                    trigger.IsSelected = true;
                    dgv.ClearSelection();
                    dgv.Rows[trigger.RowIndex].Selected = true;
                    menuToClose.FadeOut();
                    menuToClose.VisibleChanged += DisposeWhenHidden;
                    menus[level] = null;
                }
            }

            DisposeMenu(menus[menuTriggered.Level]);
            menus[menuTriggered.Level] = menuTriggered;
            AdjustSubMenusLocationAndSize();
            menus[menuTriggered.Level].FadeIn();
            AdjustSubMenusLocationAndSize();
            IsAnyMenuActive();
        }

        bool IsAnyMenuActive()
        {
            bool isAnyMenuActive = Form.ActiveForm is Menu;
            if (!isAnyMenuActive)
            {
                menus[0].SetTitleColorDeactive();
                CheckMenuOpenerStop(keyboardInput.iMenuKey, 
                    keyboardInput.iRowKey);
                keyboardInput.ClearIsSelectedByKey();
            }
            else
            {
                menus[0].SetTitleColorActive();
            }

            return isAnyMenuActive;
        }

        MenuData ReadMenu(BackgroundWorker worker, string path, int level)
        {
            MenuData menuData = new MenuData();
            menuData.RowDatas = new List<RowData>();
            menuData.Validity = MenuDataValidity.Invalid;
            menuData.Level = level;
            if (!worker.CancellationPending)
            {
                string[] directories = new string[] { };

                try
                {
                    directories = Directory.GetDirectories(path);
                    Array.Sort(directories, new WindowsExplorerSort());
                }
                catch (UnauthorizedAccessException)
                {
                    Log.Info($"UnauthorizedAccessException:'{path}'");
                }
                catch (Exception ex)
                {
                    Log.Error($"path:'{path}'", ex);
                }

                foreach (string directory in directories)
                {
                    if (worker != null && worker.CancellationPending)
                    {
                        break;
                    }

                    bool hiddenEntry = false;
                    if (FolderOptions.IsHidden(directory, ref hiddenEntry))
                    {
                        continue;
                    }

                    RowData menuButtonData = ReadMenuButtonData(directory, false);
                    menuButtonData.ContainsMenu = true;
                    menuButtonData.HiddenEntry = hiddenEntry;
                    string resolvedLnkPath = string.Empty;
                    menuButtonData.ReadIcon(true, ref resolvedLnkPath);
                    menuData.RowDatas.Add(menuButtonData);
                }
            }

            if (!worker.CancellationPending)
            {
                string[] files = new string[] { };

                try
                {
                    files = Directory.GetFiles(path).Where(p =>
                               !Path.GetFileName(p).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) && // Windows folder settings, e.g. Win10 "desktop.ini", Win2003 "Desktop.ini"
                               !Path.GetFileName(p).Equals("thumbs.db", StringComparison.OrdinalIgnoreCase) // Windows thumbnail cache
                            ).ToArray();
                    Array.Sort(files, new WindowsExplorerSort());
                }
                catch (UnauthorizedAccessException)
                {
                    Log.Info($"UnauthorizedAccessException:'{path}'");
                    menuData.Validity = MenuDataValidity.NoAccess;
                }
                catch (Exception ex)
                {
                    Log.Error($"path:'{path}'", ex);
                }

                foreach (string file in files)
                {
                    if (worker != null && worker.CancellationPending)
                    {
                        break;
                    }

                    bool hiddenEntry = false;
                    if (FolderOptions.IsHidden(file, ref hiddenEntry))
                    {
                        continue;
                    }

                    RowData menuButtonData = ReadMenuButtonData(file, false);
                    string resolvedLnkPath = string.Empty;
                    if (menuButtonData.ReadIcon(false, ref resolvedLnkPath))
                    {
                        menuButtonData = ReadMenuButtonData(resolvedLnkPath, true, menuButtonData);
                        menuButtonData.ContainsMenu = true;
                        menuButtonData.HiddenEntry = hiddenEntry;
                    }

                    menuData.RowDatas.Add(menuButtonData);
                }
            }

            if (!worker.CancellationPending)
            {
                if (menuData.Validity == MenuDataValidity.Invalid)
                    menuData.Validity = MenuDataValidity.Valid;
            }

            return menuData;
        }

        RowData ReadMenuButtonData(string fileName,
            bool isResolvedLnk, RowData menuButtonData = null)
        {
            if (menuButtonData == null)
            {
                menuButtonData = new RowData();
            }
            menuButtonData.IsResolvedLnk = isResolvedLnk;

            try
            {
                menuButtonData.FileInfo = new FileInfo(fileName);
                menuButtonData.TargetFilePath = menuButtonData.FileInfo.FullName;
                if (!isResolvedLnk)
                {
                    menuButtonData.SetText(menuButtonData.FileInfo.Name);
                    menuButtonData.TargetFilePathOrig = menuButtonData.FileInfo.FullName;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"fileName:'{fileName}'", ex);
            }

            return menuButtonData;
        }

        private void Dgv_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            DataGridView dgv = (DataGridView)sender;
            DataGridView.HitTestInfo hitTestInfo;
            hitTestInfo = dgv.HitTest(e.X, e.Y);
            if (hitTestInfo.RowIndex > -1 &&
                dgv.Rows.Count > hitTestInfo.RowIndex)
            {
                RowData trigger = (RowData)dgv.Rows[hitTestInfo.RowIndex].Tag;
                trigger.DoubleClick();
            }
        }

        private void Dgv_MouseDown(object sender, MouseEventArgs e)
        {
            DataGridView dgv = (DataGridView)sender;
            DataGridView.HitTestInfo hitTestInfo;
            hitTestInfo = dgv.HitTest(e.X, e.Y);
            if (hitTestInfo.RowIndex > -1 &&
                dgv.Rows.Count > hitTestInfo.RowIndex)
            {
                RowData trigger = (RowData)dgv.Rows[hitTestInfo.RowIndex].Tag;
                trigger.MouseDown(dgv, e);
            }
        }

        private void Dgv_MouseEnter(object sender, DataGridViewCellEventArgs e)
        {
            DataGridView dgv = (DataGridView)sender;
            dgvFromLastMouseEvent = dgv;
            cellEventArgsFromLastMouseEvent = e;

            if (!keyboardInput.InUse)
            {
                keyboardInput.ClearIsSelectedByKey();
                keyboardInput.Select(dgv, e.RowIndex);

                CheckMenuOpenerStart(dgv, e.RowIndex);
            }
        }

        private void CheckMenuOpenerStart(DataGridView dgv, int rowIndex)
        {
            if (rowIndex > -1 &&
                dgv.Rows.Count > rowIndex)
            {
                RowData trigger = (RowData)dgv.Rows[rowIndex].Tag;
                trigger.IsSelected = true;
                dgv.Rows[rowIndex].Selected = true;
                Menu menuFromTrigger = (Menu)dgv.FindForm();
                Menu menuTriggered = trigger.SubMenu;
                int level = menuFromTrigger.Level + 1;

                if (trigger.ContainsMenu &&
                    level < MenuDefines.MenusMax &&
                    !menus[0].IsFadingOut &&
                    !menuFromTrigger.IsFadingOut &&
                    (menus[level] == null ||
                    menus[level] != menuTriggered))
                {
                    trigger.StopLoadMenuAndStartWaitToOpenIt();
                    trigger.StartMenuOpener();

                    if (trigger.Reading.IsBusy)
                    {
                        trigger.RestartLoading = true;
                    }
                    else
                    {
                        menuNotifyIcon.LoadingStart();
                        trigger.Reading.RunWorkerAsync(level);
                    }
                }
            }
        }

        private void Dgv_MouseLeave(object sender, DataGridViewCellEventArgs e)
        {
            DataGridView dgv = (DataGridView)sender;

            if (!keyboardInput.InUse)
            {
                Menu menu = (Menu)dgv.FindForm();
                CheckMenuOpenerStop(menu.Level, e.RowIndex, dgv);
            }

            dgvFromLastMouseEvent = null;
            cellEventArgsFromLastMouseEvent = null;
        }

        private void CheckMenuOpenerStop(int menuIndex, int rowIndex, DataGridView dgv = null)
        {
            Menu menu = menus[menuIndex];
            if (menu != null &&
                rowIndex > -1)
            {
                if (dgv == null)
                {
                    dgv = menu.GetDataGridView();
                }
                if (dgv.Rows.Count > rowIndex)
                {
                    RowData trigger = (RowData)dgv.Rows[rowIndex].Tag;
                    if (trigger.Reading.IsBusy)
                    {
                        if (!trigger.IsContextMenuOpen)
                        {
                            trigger.IsSelected = false;
                            dgv.Rows[rowIndex].Selected = false;
                        }
                        trigger.Reading.CancelAsync();
                    }
                    else if (trigger.ContainsMenu && !trigger.IsLoading)
                    {
                        trigger.IsSelected = true;
                        dgv.Rows[rowIndex].Selected = true;
                    }
                    else
                    {
                        if (!trigger.IsContextMenuOpen)
                        {
                            trigger.IsSelected = false;
                            dgv.Rows[rowIndex].Selected = false;
                        }
                    }
                    if (trigger.IsLoading)
                    {
                        trigger.StopLoadMenuAndStartWaitToOpenIt();
                        trigger.IsLoading = false;
                    }
                }
            }
        }

        private void Dgv_SelectionChanged(object sender, EventArgs e)
        {
            DataGridView dgv = (DataGridView)sender;
            foreach (DataGridViewRow row in dgv.Rows)
            {
                RowData rowData = (RowData)row.Tag;
                if (rowData.IsSelectedByKeyboard)
                {
                    row.DefaultCellStyle.SelectionBackColor =
                        MenuDefines.KeyBoardSelection;
                    row.Selected = true;
                }
                else if (rowData.IsSelected)
                {
                    row.DefaultCellStyle.SelectionBackColor =
                        MenuDefines.FolderOpen;
                    row.Selected = true;
                }
                else
                {
                    rowData.IsSelected = false;
                    row.Selected = false;
                }
            }
        }

        IEnumerable<Menu> Menus()
        {
            return menus.Where(m => m != null);
        }

        void MenusFadeOut()
        {
            if (messageFilterAdded)
            {
                Application.RemoveMessageFilter(messageFilter);
                messageFilterAdded = false;
            }

            Menus().ToList().ForEach(menu =>
            {
                if (menu.Level > 0)
                {
                    menus[menu.Level] = null;
                }
                menu.FadeOut();
            });
        }

        Menu CreateMenu(MenuData menuData, string title = null)
        {
            Menu menu = new Menu();
            if (title != null)
            {
                if (title == string.Empty)
                {
                    title = Path.GetPathRoot(Config.Path);
                }

                menu.SetTitle(title);
                menu.UserClickedOpenFolder += OpenFolder;
                void OpenFolder()
                {
                    Process.Start("explorer.exe", Config.Path);
                }
            }
            menu.Level = menuData.Level;
            menu.MouseWheel += AdjustSubMenusLocationAndSize;
            DataGridView dgv = menu.GetDataGridView();
            dgv.CellMouseEnter += Dgv_MouseEnter;
            dgv.CellMouseLeave += Dgv_MouseLeave;
            dgv.MouseDoubleClick += Dgv_MouseDoubleClick;
            dgv.MouseDown += Dgv_MouseDown;
            dgv.SelectionChanged += Dgv_SelectionChanged;
            menu.KeyPress += keyboardInput.KeyPress;
            menu.CmdKeyProcessed += keyboardInput.CmdKeyProcessed;
            menu.Activated += Activated; 
            void Activated(object sender, EventArgs e)
            {
                menus[0].SetTitleColorActive();
            }
            menu.Deactivated += fastLeave.Start;
            menu.VisibleChanged += DisposeWhenHidden;
            AddItemsToMenu(menuData.RowDatas, menu);
            return menu;
        }



        private void AddItemsToMenu(List<RowData> data, Menu menu)
        {
            foreach (RowData rowData in data)
            {
                CreateMenuRow(rowData, menu);
            }
        }

        private void CreateMenuRow(RowData rowData, Menu menu)
        {
            rowData.SetData(rowData, menu.GetDataGridView());
            rowData.OpenMenu += OpenSubMenu;
            rowData.Reading.WorkerSupportsCancellation = true;
            rowData.Reading.DoWork += ReadMenu_DoWork;
            void ReadMenu_DoWork(object senderDoWork,
                DoWorkEventArgs eDoWork)
            {
                int level = (int)eDoWork.Argument;
                BackgroundWorker worker = (BackgroundWorker)senderDoWork;
                rowData.RestartLoading = false;
                eDoWork.Result = ReadMenu(worker, rowData.TargetFilePath, level);
            }

            rowData.Reading.RunWorkerCompleted += ReadMenu_RunWorkerCompleted;
            void ReadMenu_RunWorkerCompleted(object senderCompleted,
                RunWorkerCompletedEventArgs e)
            {
                MenuData menuData = (MenuData)e.Result;
                if (rowData.RestartLoading)
                {
                    rowData.Reading.RunWorkerAsync(menuData.Level);
                }
                else
                {
                    menuNotifyIcon.LoadingStop();
                    menuNotifyIcon.LoadWait();
                    if (menuData.Validity != MenuDataValidity.Invalid)
                    {
                        menu = CreateMenu(menuData);
                        if (menuData.RowDatas.Count > 0)
                        {
                            menu.SetTypeSub();
                        }
                        else if (menuData.Validity == MenuDataValidity.NoAccess)
                        {
                            menu.SetTypeNoAccess();
                        }
                        else
                        {
                            menu.SetTypeEmpty();
                        }
                        menu.Tag = rowData;
                        rowData.SubMenu = menu;
                        rowData.MenuLoaded();
                    }
                    menuNotifyIcon.LoadStop();
                }
            }
        }
    }
}