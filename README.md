<p align="center">
  <img src=".Image/Dark%20Banner.svg" alt="SuperRename" width="657">
</p>

A lightweight Windows app for renaming files and folders quickly, one at a time, while you see what you are renaming. It also helps you clean up and organize large numbers of files: you can go inside folders, rename or delete what is in them, and check everything before anything changes.

Select your files in File Explorer, right-click, choose **SuperRename**, go through them one by one, then press **Finish**. Nothing on your disk changes until you press Finish.

## What it does

- **Rename one by one with a preview.** Images, text files, PDFs, folders and zip files are shown as you go. Type the new name and press **Enter** to move to the next item.
- **Edit the extension separately** Use at your own risk changing an extension can break a file use a converter if needed they are plenty on the internet and even self hosted one.
- **Mark items for deletion.** Files, folder and zip goes all to the recycle bin, unless the item is too big then window permanently deletes them which is the default window behaviour.
- **Clean up inside folders.** When the item is a folder, its contents are listed on the left. Double-click a subfolder to go deeper, then rename or delete anything inside. Zip files can be browsed and previewed too **but cannot be edited** because it would need to recompress everything everytime.
- **Review before applying.** A table shows every change. You can edit names, undo a change, or double click to go back to its edit page.
- **Safe by default.** Names that are invalid or already taken are flagged. If a rename fails, the other renames are undone.

## Requirements

- 64-bit Windows 10 (version 2004 or later) or Windows 11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64) to run the app
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) only if the app needs to be built (see below)

## Install

1. Put the `SuperRename` folder where you want to keep it, for example `C:\Tools\SuperRename`. The right-click menu points to this location, so if you move the folder later, run the install script again.
2. Open PowerShell in that folder.
3. Run **one** of these two scripts:

   **Keep the Windows 11 menu** (recommended if you are unsure). SuperRename appears under *Show more options* (or Shift + right-click):

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install-win11.ps1
   ```

   **Use the classic Windows 10 menu.** SuperRename appears directly in the first right-click menu. This changes the right-click menu for all apps, not only SuperRename:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install-win10-menu.ps1
   ```

   This version needs File Explorer to restart. The script asks before doing it, because restarting closes your open Explorer windows.

No administrator rights are needed. The scripts only change settings for your own Windows account.

If `publish\SuperRename.exe` does not exist yet, the install script builds it first. This step needs the .NET 10 SDK.

## Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

This removes SuperRename from the right-click menu. If the install script switched you to the Windows 10 menu, uninstalling restores the Windows 11 menu and asks before restarting File Explorer. A Windows 10 menu that was set up outside SuperRename is left unchanged. The program files are not deleted; you can remove the folder yourself afterwards.

## How to use

1. In File Explorer, select one or more files, folders or zip files.
2. Right-click and choose **SuperRename**.
3. For each item, type the new name and press **Enter**, or click **Delete** to mark it for the Recycle Bin.
4. For a folder, use the list on the left to go inside it. Click a file to select it, double-click a folder to open it, and use the arrow at the top to go back to the main folder.
5. Click **Review all** to check every change.
6. Click **Finish** to apply everything. Click **Cancel** to close without changing anything.

## Build from source

```powershell
dotnet publish SuperRename -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Run the tests:

```powershell
dotnet test SuperRename.Tests
```
