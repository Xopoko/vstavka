# Manual acceptance checklist

Use a separate user account and sample data, never your own clipboard history.
For every result, note the app version or commit, how it was installed and the
system version.

## Microsoft Store app (Windows)

Note whether you tested the local build or the Microsoft Store version.

1. **First run.** Close the welcome window and confirm that history is not
   running. Open Vstavka again, choose a language and enable history. Copy text,
   a link and a color value in Notepad. Open the panel with **Ctrl+Shift+V** and
   find the items with search and the type filters. Pin one item. Switch the
   shortcut to **Ctrl+Alt+V** and then **Alt+V** in Settings and check each one.
   **Esc** hides the panel and leaves Vstavka in the notification area.
2. **Pasting.** Check **Enter** and double-click in an editor. Paste multi-line
   text, an image, and files copied in File Explorer. Check **Shift+Enter** for
   file paths. Move one of the files, paste it from history and check the
   message. If an elevated window blocks automatic pasting, check manual paste.
3. **Collections.** Create a collection with **+**, rename it and change its
   color. Add a card from its context menu, move it to another collection and
   remove it again. Items in a collection must not appear under **All** or the
   type filters. Delete a collection and confirm that its items are gone after a
   restart. Press **Enter** in the name field and confirm that it saves the
   collection instead of pasting a card.
4. **Source icons.** Copy from two or three apps and check that each card shows
   the source app icon, or the neutral glyph when the icon is not available
   (for example, an app running as administrator).
5. **Sounds.** Listen to copy, paste from the panel, **Ctrl+V** and
   **Shift+Insert**. Change the volume and turn each sound off separately.
   Ordinary typing must stay silent.
6. **Layout.** Check the panel on a narrow screen and at 100%, 125% and 150%
   scaling. Search and buttons stay reachable while the categories scroll.
   Change the theme and accent, then cancel. Save a right-to-left language and
   check the direction of the interface, paths and links. Check keyboard focus:
   rings appear for keyboard navigation and not after a mouse click.
7. **Storage.** Copy a phrase while saving is paused, then copy from an app in
   the exclusion list. Neither item may appear. Delete one card, restart the app
   and confirm it does not come back. Clear history in Settings and check the
   result after a restart.
8. **Installation and startup.** Check the local preview and the Store version
   separately. Sign in to Windows with startup enabled, disable Vstavka in the
   Windows startup list and sign in again. When updating from an older Store
   version, check that history, pins, collections and settings, including the
   chosen shortcut, are kept. For a new package, record its hash and the results
   of install, update, uninstall and the Windows App Certification Kit.

## Cross-platform app

Test on Windows, macOS and Ubuntu. On Linux, note whether the session is
Wayland or X11.

1. **First-run guide.** Start with an empty data folder. Go through the four
   steps with the keyboard and with the mouse, go back from each step, and quit
   in the middle: the next start resumes at the same step. Record a new
   shortcut, then start recording again and cancel it with **Esc**. The logo,
   the panel preview and the shortcut keys animate; with reduced motion turned
   on in the system, everything appears at once.
2. **Permissions.** On macOS, start without clipboard access and Accessibility,
   grant each in System Settings and check that the guide notices without a
   restart. Without Accessibility, pasting must copy the item and say so. On
   Ubuntu with Wayland, let the guide install the GNOME helper, sign out and
   back in, and check that the guide resumes and the shortcut works.
3. **Panel.** The panel opens at the bottom of the active screen; on macOS it
   also appears over full-screen apps. Check search, filters, pins, quick
   paste with **Ctrl+1 … 9** (**⌘1 … 9** on macOS), **Shift+Enter**, delete
   (**Delete**, or **⌫** on macOS), **Esc** and the tray or menu bar icon.
4. **Pasting.** Paste text, an image and files into a text editor, a browser
   and a terminal. Note every app where automatic paste does not work.
5. **Privacy.** Pause saving, then copy from an excluded app and from a password
   manager (KeePassXC on Linux, 1Password on macOS). None of it may appear.
   Restart and check that history is kept. Without a key store (no GNOME
   Keyring, or the Keychain request denied), Settings must say that history is
   kept in memory only.
6. **Start at login.** Turn it on in Settings, sign out and back in, then turn
   it off and check again.
