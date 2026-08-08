# Code style

- All comments, logs must be written in English.
- Documentation must be short.
- Summary of modules, properties and fields should be added to project's CLAUDE.md instead of quick documentation section.
- Inner code comments are allowed only inside the code to clarify complex logic.
- Every project's CLAUDE.md headers structure must repeat the file structure of folders.
- Single-line statements ('return', 'continue', 'break', etc.) must be written on the separate line.
- Every module (class, record etc.) must be stored in separate file.
- Namespaces should be file-scoped.
- All background routines must be places in Routines folder~~~~
- All models must be placed in Models folder~~~~
- Options models must be placed in Options folder
- All main logic modules nmust be placed in Pipeline folder
- All helpers and infrastructure modules must be placed in Infrastructure folder
- All abstractions should be placed in Abstractions folder
- All storages should be placed in Storages folder

# Instructions

- Context from CLAUDE.md files regarding requested key words should always be loaded before studying the code and understanding the project's structure.
- CLAUDE.md files should be loaded in the following order: from the root folder to the deepest subfolder.
- CLAUDE.md files should always be updated if its contents are becoming not up to date with any changes in the project's structure or code.