# Privacy Policy

**Last updated: 2026-05-29**

**Meridian** is a free, open-source Windows desktop application for viewing and
managing your calendars and tasks across supported calendar and task providers.
It runs entirely on your own device and has no backend servers of its own.

## Summary

Meridian does not have any servers. It does not send your data to us or to any
third party other than the calendar and task providers you choose to connect.
All of your data stays on your device, except for the direct requests Meridian
makes to those providers on your behalf.

## What Data Meridian Accesses

When you connect an account, Meridian accesses only what is needed to display
and manage your schedule:

- **Calendar data** — your calendars and their events (titles, times,
  locations, attendees, reminders, and similar event details). At your
  request, Meridian can create, modify, and delete events.
- **Task data** — your task lists and their tasks. At your request,
  Meridian can create, modify, and delete tasks.
- **Basic profile information** — your account's email address and name, used
  to label connected accounts in the app and to sign in.

Meridian requests only the access required for these features. The specific
permissions shown when you connect an account correspond directly to the
calendar, task, and basic-profile access described above.

## How Your Data Is Stored and Used

- **Everything is local.** Calendar data, task data, profile labels, and
  authentication tokens are stored only on your device (for example, under
  `%APPDATA%\Meridian\`). Meridian has no servers and never uploads this data
  anywhere.
- **Authentication tokens** are used solely to communicate with the respective
  provider's APIs on your behalf. Your provider credentials (passwords) are
  never seen, transmitted, or stored by Meridian — authentication happens
  directly with the provider through OAuth 2.0.
- **No analytics, no tracking, no advertising.** Meridian does not collect
  usage analytics, does not profile you, and contains no advertising or
  third-party tracking of any kind.
- Your calendar and task data leaves your device only as part of the direct API
  requests Meridian makes to the provider that owns that data, in order to read
  or update it at your request.
- **Sharing and disclosure.** Meridian does not share or disclose your data with
  any third party. The only network transmission of your data is between your
  device and the owning provider's own APIs, to read or modify the data you
  asked Meridian to act on.

## Deleting Your Data

You are always in control of the locally stored data:

- **Disconnect an account** in the app to remove its stored tokens and cached
  data for that account.
- **Remove everything** by deleting the `%APPDATA%\Meridian\` folder, which
  clears all locally cached calendar/task data and all stored tokens.
- You can also revoke Meridian's access at any time from your provider's own
  account-security settings; doing so invalidates the stored tokens.

Because Meridian stores nothing on any server, there is no remote copy of your
data for us to retain or delete.

## Third-Party Services

Meridian communicates only with the APIs of the calendar and task providers you
explicitly choose to connect. No other third parties receive your data. As
support for additional providers is added over time, the same principles in
this policy apply to each of them: local-only storage, direct communication
with that provider only, and no data sent to us.

## Google API Limited Use

When you connect a Google account, Meridian's use and transfer to any other app
of information received from Google APIs adheres to the
[Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy),
including its **Limited Use** requirements. Specifically, data obtained from
Google APIs:

1. is used only to provide or improve Meridian's user-facing calendar and task
   features on your device;
2. is not transferred to others except as necessary to provide or improve those
   user-facing features, to comply with applicable law, or as part of a merger
   or acquisition with appropriate notice;
3. is not used or transferred for advertising, ad personalization, or sale; and
4. is not read by humans unless we have your affirmative consent, it is
   necessary for security purposes (such as investigating abuse) or to comply
   with applicable law, or the data has been aggregated and anonymized.

## Open Source

Meridian is fully open source. You can review all source code at
https://github.com/Phoenix-/meridian.

## Changes to This Policy

If this policy changes, the "Last updated" date above will be revised. Material
changes will be noted in the project's release notes.

## Contact

If you have questions about this privacy policy, please open an issue at
https://github.com/Phoenix-/meridian/issues.
