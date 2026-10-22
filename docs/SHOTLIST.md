# Screenshots that can't be generated

Every picture in the user guide is made automatically by DocShots (`tools/TrajectoryLogTools.DocShots`)
from the apps' real windows, except Eclipse's own screens, which only exist inside Eclipse. The guide
describes them in text. These are the only ones worth adding, and both are optional:

| # | File (put it in `docs/user/images/`) | What to capture | Used on |
|---|---|---|---|
| 1 | `eclipse-tools-scripts.png` | Eclipse's **Tools > Scripts** dialog with `LaunchTrajectoryLogTools.cs` and `LaunchTrajectoryLogConverter.cs` in the list and one selected, **Run** visible. No patient open (or a test patient), nothing identifying in the background. Crop to the dialog. | `getting-started/eclipse-launchers.md`, after step 5 |
| 2 | `eclipse-import-mlc.png` | Only if the department wants the Eclipse import procedure in the guide: the Eclipse dialog for importing an MLC file onto a beam, with a test plan. | `how-to/rebuild-mlc.md`, once the import procedure is supplied |

Take them at 100 % or 150 % display scale, PNG, light Eclipse theme. To use one, add it to the page, e.g.

```markdown
![Eclipse: Tools > Scripts](../images/eclipse-tools-scripts.png){ .shot }
```

DocShots never deletes or overwrites a file it doesn't make, so these stay.
