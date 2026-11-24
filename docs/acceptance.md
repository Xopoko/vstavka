# Manual release checks

Use sample data in a separate user account. Record the app version or commit,
installation source and OS version. On Ubuntu, record whether you used a
GNOME Wayland or X11 session.

## All builds

1. **First run.** Use the keyboard and mouse to move through the guide. Quit
   midway and check that the guide resumes at the same step. Record a new
   shortcut, then cancel another recording with **Esc**. Check the animations
   with reduced motion turned on and off.
2. **Panel.** Open the panel on the active screen. Try search, the filters,
   pinning, arrow navigation, the numbered shortcuts, **Delete** (**⌫** on
   macOS), **Esc** and the tray or menu bar icon. Copy from multiple apps and
   check the source icons, including the fallback when an icon is unavailable.
3. **Clipboard.** Use **Enter** and double-click to select text, an image and
   files. Try an editor, browser and terminal. Check **Shift+Enter** for file
   paths. Move a copied file and check the message when selecting its card.
4. **Collections.** Create a collection with **+**, rename it and change its
   color. Move a card between collections and back to history. Collection
   items should stay out of the history filters. Press **Enter** in the name
   field to save the name. Delete a collection and check after restarting that
   its contents are gone.
5. **Layout.** Try a narrow screen and several scaling levels. Check that
   search and buttons stay reachable as the filter row scrolls. Check keyboard
   focus. Change theme and accent, then cancel. Save a right-to-left language
   and check the direction of both the interface and clipboard content.
6. **History.** Check that a copy made while paused, a copy from an excluded
   app and a copy marked private stay out of history. Delete a card, then clear
   history, and check each result after a restart. Check that clearing includes
   pinned and collection items.
7. **Start at login.** Enable it, sign out and back in, then disable it and
   repeat.

## Windows

- On a fresh Store install, close the guide before choosing **Enable history**
  and check that clipboard history remains off. Reopen the app, enable history
  and check that it records new copies.
- Try automatic paste in an elevated window and check that manual paste works
  if Windows blocks automatic paste. Check copy, panel paste, **Ctrl+V** and
  **Shift+Insert** sounds, the volume, and each sound switch. Ordinary typing
  should stay silent.
- Disable startup in Windows Settings and check that the app respects the
  change. When updating from the WPF release through Microsoft Store, verify
  history, pins, collections, settings and the saved shortcut. Record the
  package hash and the results of install, update, uninstall and Windows App
  Certification Kit checks.

## macOS

- Check clipboard access on a macOS release that requires it. For the local
  build, grant Accessibility and try automatic paste. Without Accessibility,
  confirm the app copies the item for manual paste. The App Store build
  should only copy the item for **⌘V**.
- Open the panel while another app is full screen. Deny access to the history
  key on a test profile and check that Settings reports in-memory history.

## Ubuntu

- On GNOME Wayland, install the GNOME helper from the guide, sign out and back
  in, and confirm the guide resumes and the shortcut opens the panel.
- With Secret Service unavailable, check that Settings reports in-memory
  history.
