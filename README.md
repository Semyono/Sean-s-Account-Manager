Sean's Account Manager

A personal Roblox account manager built in C# and WPF. Manage multiple accounts.

Features

Accounts

Add accounts through an embedded login window or by pasting a cookie directly. Cookies are encrypted locally with Windows DPAPI. You can drag and drop to reorder accounts, add notes, and see which cookies are still valid. There's also a Select All button and your selections are remembered between sessions.

Launching

Join by Place ID, Job ID, or a private server link (VIP link, share link, or a raw code). There's also a Smallest Server button that finds the server with the most open slots. You can launch multiple accounts at once with a delay between each one, and pick which launcher to use (Default Roblox, Fishstrap, Bloxstrap, Froststrap, Voidstrap, Bubblestrap, Luczystrap, or a custom exe path).

Multi Roblox

Run more than one Roblox window at the same time using Sysinternals Handle64.

Anti AFK

Sends a key to all open Roblox windows on a timer so you don't get kicked for being idle. You can bind any key you want, set how many times it presses, and there's a Test Now button to check it works before relying on it.

Auto Rejoin

Watches an account's window and relaunches it automatically if it closes or crashes.

Roblox Tweaks

Force a framerate cap and lock it. There's also a hard RAM cap per Roblox process, enforced by Windows.

Console

A live log with filters so you can see what the app is doing at any time.

Installation

Download the latest release from the Releases page and Run the exe.

Windows might warn you that the file isn't signed. Click More info then Run anyway. This is normal for small open source tools that don't have a paid certificate.

The app also asks for admin permissions on launch. That's only needed for Multi Roblox, nothing else uses it.


Building from source

You need the .NET 10 SDK and Visual Studio or just the dotnet CLI.

git clone https://github.com/<your-username>/Seans_Account_Manager.git
cd Seans_Account_Manager
dotnet build

To build a single file release:

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

Data

Everything is stored locally in %AppData%\SeansAccountManager. Cookies are encrypted and tied to your Windows account.
