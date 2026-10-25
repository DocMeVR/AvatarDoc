# Lessons

What building AvatarDoc taught, in plain words: what happened, what we learned, and why it matters. The technical record is in `design.md`.

## Other tools' parameters (October 2026)

**What happened.** DocMe Skin Touch ships a prefab with an AvatarDoc menu: three toggles that drive Skin Touch's own parameters. Every buyer saw three warnings saying nothing on the avatar reads those parameters, although the upload worked. Skin Touch builds the animation that reads them during the upload, after AvatarDoc has looked.

**What we learned.**

- Tools that do their work during the upload cannot see each other in the editor unless they say what they use. NDMF has a shared way to say it: each tool reports its parameters, and any tool can ask. Skin Touch now reports its three toggles, and AvatarDoc counts a reported parameter as used.
- NDMF keeps only the first report of each name. AvatarDoc's own report (an AvatarDoc on the avatar root comes first) and the avatar's own parameter list both hid Skin Touch's report. AvatarDoc now stays quiet while it asks, and asks object by object below the avatar root.
- Reporting a parameter is not reading it. A VRChat contact reports the parameter it writes, so VRChat's own components do not count.
- The first fix trusted Skin Touch to add its parameters, so AvatarDoc stopped adding them. The menu toggles then lost their parameters: Modular Avatar clears every menu parameter that is not declared yet, and Skin Touch declares its parameters later in the upload. Skin Touch's test, which runs the whole upload, caught it. AvatarDoc's tests ran only AvatarDoc's own step and could not.

**Why it matters.** The order of the upload steps decides what each tool can rely on. A check that looks at one tool alone gives false warnings, and a fix that trusts another tool's timing breaks quietly. One test that runs the whole upload is worth many that run one step.
