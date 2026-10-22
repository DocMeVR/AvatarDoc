# Changelog

## [0.7.0](https://github.com/DocMeVR/AvatarDoc/compare/0.6.0...0.7.0) (2026-10-22)


### Features

* **groups:** several AvatarDoc components on one avatar without collisions ([8a4488c](https://github.com/DocMeVR/AvatarDoc/commit/8a4488c32c2380e9390ddca6c3c8bfe64e6d691b))


### Behaviour change

* **groups:** an exclusive group belongs to its component now. When two AvatarDoc components on one avatar each have a group with the same name, they build two groups, so a toggle of one no longer turns off the toggles of the other. The inspector names both components in a note, and its Join them button makes them one group again.

## [0.6.0](https://github.com/DocMeVR/AvatarDoc/compare/0.5.0...0.6.0) (2026-10-21)


### Features

* **copy:** copy an AvatarDoc menu to another version of the avatar ([c2e7b24](https://github.com/DocMeVR/AvatarDoc/commit/c2e7b24a5c74b631f76b295228018d34c64b3a93))
* **gestures:** facial expressions on hand gestures, with face tracking in mind ([30de19b](https://github.com/DocMeVR/AvatarDoc/commit/30de19b5f3b8e607fc74e1cefcbeea978339d623))
* **poiyomi:** keep material animations working after Poiyomi locks the material ([4df8dcc](https://github.com/DocMeVR/AvatarDoc/commit/4df8dcc7d39f5409605541a991c0440d9bc2315f))
* **preview:** try touch triggers with a test hand in the Scene view ([2d679fc](https://github.com/DocMeVR/AvatarDoc/commit/2d679fcda57ce10b27cebbc82d6c3fc1f550aea2))
* **triggers:** a Fade that follows a Float parameter ([30de19b](https://github.com/DocMeVR/AvatarDoc/commit/30de19b5f3b8e607fc74e1cefcbeea978339d623))
* **ui:** add a blend shape or material property to AvatarDoc from where you set it ([4d1e1c7](https://github.com/DocMeVR/AvatarDoc/commit/4d1e1c71939cf4fa7c14b532e1e90b8186ab55de))

## [0.5.0](https://github.com/DocMeVR/AvatarDoc/compare/0.4.1...0.5.0) (2026-10-20)


### Features

* **localization:** language files as .po ([#6](https://github.com/DocMeVR/AvatarDoc/issues/6)) ([6318630](https://github.com/DocMeVR/AvatarDoc/commit/6318630b523e094201a3d31998331179cf2c1689))

## [0.4.1](https://github.com/DocMeVR/AvatarDoc/compare/0.4.0...0.4.1) (2026-10-19)


### Bug Fixes

* **build:** reset held shapes once, then let go, so blinking and face tracking keep working ([#5](https://github.com/DocMeVR/AvatarDoc/issues/5)) ([c431f1e](https://github.com/DocMeVR/AvatarDoc/commit/c431f1e35989bc448c270150588dab6f8bfb2522))

## [0.4.0](https://github.com/DocMeVR/AvatarDoc/compare/0.3.0...0.4.0) (2026-10-11)


### Features

* **docs:** open the guide from the ? button, each message and Quick start ([fc8e41d](https://github.com/DocMeVR/AvatarDoc/commit/fc8e41de9ce1692b9267e06e977d0fbfe78886dd))
* **parameters:** add + Parameter to declare a parameter from the toolbar ([1ccfa5c](https://github.com/DocMeVR/AvatarDoc/commit/1ccfa5cf13ed5e791c91ed1984c49da8448498de))
* **shapes:** hold blink and visemes separately, and only shapes that move ([#2](https://github.com/DocMeVR/AvatarDoc/issues/2)) ([f910cd3](https://github.com/DocMeVR/AvatarDoc/commit/f910cd3b4e6bc16bffb808a5ecf132561400884a))


### Bug Fixes

* **animators:** show a declared parameter's real synced state in the merged animator panel ([fc8e41d](https://github.com/DocMeVR/AvatarDoc/commit/fc8e41de9ce1692b9267e06e977d0fbfe78886dd))
* correct plurals, missing texts and messages the docs pass found ([1ccfa5c](https://github.com/DocMeVR/AvatarDoc/commit/1ccfa5cf13ed5e791c91ed1984c49da8448498de))
* **groups:** give each member its own value in one click when two collide, and hide the parameter name members do not use ([fc8e41d](https://github.com/DocMeVR/AvatarDoc/commit/fc8e41de9ce1692b9267e06e977d0fbfe78886dd))
* **outfits:** describe the None option's tooltip truthfully ([fc8e41d](https://github.com/DocMeVR/AvatarDoc/commit/fc8e41de9ce1692b9267e06e977d0fbfe78886dd))
* **overrides:** say what an override builds instead of "Builds nothing yet" ([fc8e41d](https://github.com/DocMeVR/AvatarDoc/commit/fc8e41de9ce1692b9267e06e977d0fbfe78886dd))
* **shapes:** warn when pieces switched by different layers hold one shape, and preview what the upload does ([1ccfa5c](https://github.com/DocMeVR/AvatarDoc/commit/1ccfa5cf13ed5e791c91ed1984c49da8448498de))
* **ui:** keep every list row usable at the default inspector width ([122f905](https://github.com/DocMeVR/AvatarDoc/commit/122f905e4d9b41ac2571e25c333a168fd30f201c))
* **ui:** link the capture, drop and shape rule notes to the docs and correct two messages ([49ea699](https://github.com/DocMeVR/AvatarDoc/commit/49ea699f8fd308227aeeb78fb5fe9248f5281a10))

## [0.3.0](https://github.com/DocMeVR/AvatarDoc/compare/0.2.2...0.3.0) (2026-10-10)


### Features

* **i18n:** add Japanese and Simplified Chinese, sharing NDMF's language setting ([b6fe2a3](https://github.com/DocMeVR/AvatarDoc/commit/b6fe2a3d21c841473b12f230329316b65ae73a48))
* **pickers:** search every picker from a field at the top ([be75559](https://github.com/DocMeVR/AvatarDoc/commit/be7555979d438cdeee473992ad52f7bfacd4c304))
* **shapes:** hold the rest of a mesh at its default or at 0 ([52527b7](https://github.com/DocMeVR/AvatarDoc/commit/52527b7723bb86fd4e664b016705ab611fd44900))


### Bug Fixes

* **pickers:** show the whole shader property name beside a search result ([6edf807](https://github.com/DocMeVR/AvatarDoc/commit/6edf807e0c4d4771c9693aa34b10e8dd44aed3b9))
* polish search and languages before the first release ([98c61b7](https://github.com/DocMeVR/AvatarDoc/commit/98c61b7ffd8f80f7ef1a219ac7d035a29d28766d))
