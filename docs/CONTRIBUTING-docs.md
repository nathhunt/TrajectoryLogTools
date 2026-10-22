# Contributing to the user guide

The user guide is the offline HTML site that ships in `Help\` of both deploy folders and opens from
**Help** / F1 in both apps. Its source is `docs/user` (Markdown), built with MkDocs Material
(`mkdocs.yml` at the repository root). Every screenshot is made by DocShots from the real windows.

```text
docs/user/            the pages (Diátaxis: getting started, tutorials, how-to, reference, explanation, about)
docs/user/images/     the screenshots: made by DocShots, never by hand (except docs/SHOTLIST.md)
docs/user/assets/     icon, favicon, extra.css
docs/requirements.txt pinned MkDocs versions
mkdocs.yml            site settings and the navigation
tools/TrajectoryLogTools.DocShots/   the screenshot tool (Windows only, not deployed)
src/Help.targets      copies site/ into Help\ when publishing
src/TrajectoryLogTools.App/Services/HelpPages.cs   which page F1 opens for each window
.github/workflows/docs.yml   CI: build, test, screenshots, strict site build, publish check
```

## Set up once

```powershell
python -m pip install -r docs/requirements.txt
```

## Regenerate everything

| | VS Code (Run Task) | Command line |
|---|---|---|
| Screenshots (Windows) | **docs: screenshots** | `dotnet run -c Release --project tools/TrajectoryLogTools.DocShots` |
| Build the site | **docs: build** | `python -m mkdocs build --strict` (writes `site/`) |
| Preview while editing | **docs: serve** | `python -m mkdocs serve`, then <http://127.0.0.1:8000> |
| Publish with the guide | **publish** | `scripts\publish.ps1` (copies `site/` into both `Help\` folders) |

On GitHub, the **User guide** workflow does all of it on `windows-latest` for every push. On a working
branch it commits changed screenshots back (`[skip ci]`), and it uploads the built site as the
`user-guide-site` artifact: unzip that into `site/` to publish with the guide on a PC without Python.

DocShots writes its demo logs to `C:\Demo` (another folder with `--root <folder>`; it refuses a folder
it didn't make). Other options: `--only <scenario,...>`, `--list`, `--out <folder>`. It rewrites a PNG
only when its pixels changed, so a diff shows only real UI changes. The images made on CI are the
reference: a PC with another display scale or accent colour can differ by a few pixels.

## Add a page

1. Create the `.md` file in the right section of `docs/user` (one task per how-to page).
2. Add it to `nav:` in `mkdocs.yml`.
3. Write in NZ English, in the second person, with numbered steps and the exact on-screen labels in
   **bold**. Follow the Microsoft Writing Style Guide.
4. Something the department must supply goes in a `!!! warning "To be confirmed"` admonition.
5. If a window should open this page with F1, add it to `HelpPages.cs` (main app) or the `Help.Register`
   call in the converter's `App.xaml.cs`. Links are `.html` paths (`how-to/export.html`).
6. Run **docs: build**; it must pass with no warnings (`--strict`).

## Add a screenshot

1. In `tools/TrajectoryLogTools.DocShots/Scenarios.cs`, add a `Shoot` call to a scenario, or a new
   scenario to `All`. A scenario sets up the real window and view model with the demo logs
   (`DemoData.cs`), waits (`Ui.WaitUntil`, `Ui.Settle`), and shoots:

    ```csharp
    await s.Shoot(w, "export-window", new ShotOptions()                 // docs/user/images/export-window.png
        .Mark(1, r => Ui.Button(r, "v4.0 log (.bin)"), "left")          // numbered callout (1)
        .Mark(2, r => Ui.Text(r, "Where"), "left"));
    await s.Shoot(w, "compare-cards", new ShotOptions { Crop = r => ItemsFor(r, page.Cards) });   // part of a window
    ```

    Find elements by their text (`Ui.Button`, `Ui.Text`, `Ui.Expander`), their `x:Name` (`Named`), or
    what they're bound to (`ItemsFor`). Callouts sit `left`, `right`, `top`, `bottom`, `topleft` or `inside`.
2. Need another state? Add synthetic logs to `DemoData.cs`. **Never patient data**: build logs with the
   tests' `DeliverySimulator` / `LogBuilder`, patients `PHANTOM-nn`, plans named "(demo)".
3. Use it in a page: `![What it shows](../images/export-window.png){ .shot }`, and refer to the callouts
   in the steps: "select **Export** (6)".
4. Run **docs: screenshots** (or push and let CI make it), and look at the image.

## Release

Add a section to `docs/user/about/whats-new.md` and update the version in `docs/user/about/support.md`
and `copyright:` in `mkdocs.yml` with `<Version>` in `Directory.Build.props`.
