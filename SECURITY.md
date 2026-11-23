# Reporting security issues

Report vulnerabilities privately through **Security → Report a vulnerability** on
this GitHub repository. Include the Vstavka version, whether it is the Microsoft
Store or local build, the Windows version, and the impact. Use synthetic
clipboard content in reports. Do not attach a real `history.vstavka` file,
screenshots of your own history, or copied passwords and tokens.

## What Vstavka protects

Vstavka stores clipboard history and settings in `%LOCALAPPDATA%\Vstavka`,
encrypted with Windows Data Protection (DPAPI) for the current Windows user. This
protects the file at rest from other user accounts and from copies taken off the
PC without that account. It does not protect history from programs running under
the same Windows account: those programs can decrypt it the same way Vstavka does.

Vstavka has no network code, accounts or telemetry. It skips clipboard content
while saving is paused, content from apps in the exclusion list, and content
marked with the Windows clipboard formats `ExcludeClipboardContentFromMonitorProcessing`,
`Clipboard Viewer Ignore`, or `CanIncludeInClipboardHistory` set to zero.

Reports are most useful when they show clipboard content that should have been
skipped, history that outlives deletion or retention, data leaving the PC, or a
way for another user account to read the history.
