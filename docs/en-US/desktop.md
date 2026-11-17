# Desktop page

The "Desktop" page of the open panel brings the Windows desktop into the panel: the files, folders and shortcuts of the desktop folders (including the shared one), plus the system entries such as This PC and the Recycle Bin, drawn on this page the way the desktop draws them. The page's size is under Settings › Expanded view (width, height); its switch and order are under Settings › Expanded view.

## Taking over the desktop

- With "Take over the desktop while running" on under Settings › Expanded view (the default), Windows' own desktop icons are hidden while AxDeck runs, and the desktop's contents are only visible on this page; when AxDeck exits or the switch is turned off, the icons come back as they were.
- Hiding and restoring go through Windows' own "View › Show desktop icons" switch; on exit AxDeck restores the state from before it took over, and after an abnormal exit the next start brings the icons back too.
- With the takeover off, Windows draws the real desktop as usual and this page is a second copy of the same contents; neither side affects the other.

## What the page shows

- One list made of two desktop folders: your own desktop and the shared desktop all accounts see, where installers mostly put their shortcuts. Files marked "hidden" (such as `desktop.ini`) are not shown, as on the desktop.
- The shared half is read-only: its items can be seen and opened, but writing — renaming, deleting and the like — works only on items in your own desktop folder (the shared one usually needs administrator rights to change).
- The system entries (This PC, the Recycle Bin, the network, the control panel, the user folder) follow Windows' own settings: whichever one the desktop hides is not drawn here either, and each can be switched separately under Settings › Desktop page.
- When the desktop folders change (files added, removed or renamed), the page refreshes by itself; the collapsed panel does no work at all, and a page being opened is drawn afresh first.

## Selecting and opening

- **Click** selects an item; **Ctrl + click** adds one or takes it back; **Shift + click** selects from the focused item through the one clicked.
- **Double-click** opens, as on the desktop: a file opens with its own program, a folder opens in File Explorer, a shortcut starts its target, and a system entry is handed to the system.
- Pressing Enter with several items selected opens them one by one.

## Dragging in and out

- **Dragging in**: drop files or folders from File Explorer on this page and they are copied to the desktop (the originals stay). A bar shows where the files would land; larger copies run in the background, and the page refreshes by itself when they are done.
- A file of the same name is handled as "When a file of the same name is there" under Settings › Desktop page says: rename it, skip it, or replace the old one.
- **Dragging out**: drag an item into File Explorer or another program and you get a copy (the file stays on the desktop). System entries have no file and cannot be dragged out.

## Reordering by dragging

- Drag an item (or the selected items) onto another and drop it to insert it before or after that one: the left half of an icon inserts before it, the right half after it. A bar shows where it will land, and the dragged icons follow the pointer.
- The order drawn on this page is the real position of the desktop icons. A reorder is written back to the desktop through Windows' own icon list, so with "Take over the desktop while running" off the real desktop shows the same order; and icons arranged on the real desktop are drawn in that order here.
- Only positions change, never which items are on the desktop: the icons swap the places they already had, so nothing is thrown outside the list and no item this page does not draw (one hidden by a system entry's switch, say) is moved.
- Name order no longer decides what is drawn first: only when the desktop icon positions cannot be read (Explorer is not running, or "Auto arrange icons" is on) does the page fall back to sorting by name.
- Reordering cannot take effect while Explorer has "Auto arrange icons" on, because that mode puts written positions back at once. The panel says so as soon as a drag starts; right-click an empty spot on the desktop, open "View" and turn "Auto arrange icons" off, then come back to this page and dragging works. Until then, dragging still works for dragging items out.
- Dragging outside the page (into File Explorer or another program) still copies out, as before.

## The clipboard

- **Ctrl + C** copies the selected items, **Ctrl + X** cuts them, and **Ctrl + V** pastes the files on the clipboard onto the desktop: copied files are copied in, cut files are moved in (through the system shell, so Explorer sees it at once and deletions go to the recycle bin).
- The clipboard format is Explorer's own (CF_HDROP + Preferred DropEffect): what you copy here can be pasted into File Explorer and other programs, and files copied in Explorer can be pasted onto this page.
- Pasting items of this desktop back onto it makes no duplicates: a file already in the target folder is skipped.

## Keyboard

With the focus on this page, the keys the desktop itself takes all work:

| Key | What it does |
| --- | --- |
| Arrow keys | Move the focus; hold Shift to extend the selection |
| Home / End | First item / last item; hold Shift to extend the selection |
| Enter | Open the selected items |
| F2 | Rename the selected item |
| Delete | Send the selected items to the recycle bin |
| Ctrl + C / X / V | Copy / cut / paste |
| Ctrl + A | Select everything |
| Alt + Enter | Show the selected item's properties |
| Menu key or Shift + F10 | The selected items' system context menu |
| F5 | Refresh |
| Esc | Clear the selection |

Renaming works as on the desktop: the box preselects the main name (shortcuts without `.lnk`), Enter confirms, Esc or moving the focus away gives up. A name Windows does not take (a reserved device name, an illegal character and the like) is refused with a note instead of being applied.

## The right-click menus

- **Right-click an item**: Explorer's own menu opens, with Compressed (zipped) folders, Send to, Open with and the rest in it; system entries get theirs too. Changes made from the menu (renaming, deleting) show on this page at once.
- **Right-click the empty space**: this page's own menu — refresh, open the desktop folder, desktop page settings.

## Properties

Select an item and press **Alt + Enter** to open the same properties dialog Explorer shows; the system entries' properties can be read the same way.

## Settings

Three places hold the desktop page's settings:

- **Settings › Expanded view**: the page's width and height, and the "Take over the desktop while running" switch.
- **Settings › Desktop page › Rules**: what happens when a file of the same name is there (rename it / skip it / replace the old one), and the name under an icon (show it in full / hide `.lnk` on shortcuts / hide every extension).
- **Settings › Desktop page › Look and switches**: icon size, label size, the gaps across and down, the corner radius, whether names show, whether shortcuts carry the little arrow, and each system entry's desktop icon switch.
