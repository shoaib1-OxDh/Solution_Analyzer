# Solution Analyzer & Field Health Checker

An **XrmToolBox** tool that runs read-only health checks on your **Microsoft Dataverse / Dynamics 365** data model and shows, in colour, where the clutter is and what you can safely do about it.

> **Read-only.** The tool never creates, updates or deletes anything in your environment.

## Install

In XrmToolBox open **Configuration > Tool Library**, search for **Solution Analyzer**, tick it and click **Install**.
Requires XrmToolBox 1.2025.7.71 or later.

## The four reports

| Report | What it finds |
|---|---|
| **1 · Similar fields + fill %** | Look-alike columns on the same table (e.g. `msemr_subject` vs `xyz_subject`) and how many records actually use each one. Flags *Merge candidate* and *Empty - delete candidate*. |
| **2 · Duplicate option sets** | Choice fields with the same name on one table, near-identical local option sets across tables, and local sets that copy an existing global option set. |
| **3 · Field length usage** | Text columns whose configured max length is far bigger than the longest value stored (actual max, average and P95 length). |
| **4 · Unused fields** | Custom columns with no data in any record, checked against dependencies and references in forms, views, charts, workflows, business rules and plug-in steps. Status *Safe to delete* or *Review before deleting*. |

## How to use

1. Connect to your environment and open the tool.
2. **Step 1** - click *Load metadata & solutions*.
3. **Step 2** (optional) - expand *Scope & filters* to limit the check to specific solutions or change thresholds.
4. **Step 3** - click one of the four coloured report buttons.
5. Click the coloured **summary cards** to filter the results, use **Search**, and **Export to CSV** to share what you see.

Every row is colour-coded: **red** = clean-up candidate, **amber** = needs review, **blue** = suggestion, **green** = healthy, **grey** = no data.

## Good to know

- Large tables are handled automatically (queries are split to stay under Dataverse's 50,000-record aggregate limit).
- Dataverse cannot shrink an existing column, so report 3 is for design review.
- Web resource JavaScript, Power Automate flows, canvas apps and PCF controls are **not** scanned for references - check them before deleting a column.
- The optional delete script (report 4) has every command commented out; nothing is deleted by the tool.

Author: **Shoaib Khan**
