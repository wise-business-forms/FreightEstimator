# The Poller Monitor is the first restricted area, open only to a named list of users

**Status:** accepted.

The Poller Monitor shows raw CostPlus request data (LoginIds, destinations, errors), unlike the Reports pages, which are open to anyone who can reach the site. It is therefore the first page in this app with access control. It uses Windows authentication and an allow list of named users in a setting (`Admin:AllowedUsers`), filled in at deploy time. When the setting is empty, production denies everyone, so a missing setting cannot open the page up. Development skips the check, because Kestrel needs extra setup for Windows authentication and the FreightEstimator-only database guard already keeps a Development run off production data.

A list of users was chosen over a Windows group because no group is agreed for this, and a guessed group name in the repo is worse than a missing setting. Moving to a group later changes only how the setting is read.

A future reader should not read this as a decision to lock down Reports. It is not one: Reports stay as they are until someone decides otherwise.
