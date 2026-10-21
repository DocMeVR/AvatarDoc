# AvatarDoc glossary

The words AvatarDoc uses for VRChat and Unity things, and how each language writes them. Use these words, so one
thing has one name everywhere in a language, and add a row when you bring in a new term. An empty cell means no single
word was agreed: the translations say it in a few words. See `translating.md` beside this file for how to translate.

Tone. Japanese: sentences (tooltips, messages, notes) in です・ます調, plain and polite, saying what happened and then
what to do; labels, buttons and chips are short noun phrases; the creator's names go in 「」. Simplified Chinese:
neutral and concise, no exclamation marks; messages end with 请…… for the fix; 你, not 您, as Modular Avatar writes;
names go in “”, with a half-width space between Chinese and Latin words, numbers or placeholders.

## VRChat and Unity terms

| English | Japanese | Simplified Chinese | Note |
|---|---|---|---|
| Avatar | アバター | Avatar | Chinese keeps it in English, as Modular Avatar does. |
| Avatar Descriptor | Avatar Descriptor | Avatar Descriptor | The VRChat component on the avatar's root object; a component name, kept. |
| Expressions Menu | Expressions Menu | Expressions 菜单 | VRChat's in game menu, and the asset that holds it. |
| Expression Parameters | Expression Parameters | Expression Parameters | The asset that lists an avatar's parameters. |
| Menu | メニュー | 菜单 | |
| Root menu | ルートメニュー | 根菜单 | The top of the menu. |
| Submenu | サブメニュー | 子菜单 | |
| Menu item | メニューアイテム | 菜单项 | An item of the avatar's own menu or of another tool. |
| Toggle | トグル | 开关 | A menu control that turns something on or off. |
| Radial | ラジアル | 转盘 | A menu control set with a dial from 0 to 100 percent (Radial Puppet). Never 径向 in Chinese; 轮盘 is the Wheel. |
| Button | ボタン | 按钮 | A menu control that is on only while held. |
| Control | コントロール | 控件 | One item of an Expressions Menu; also メニューコントロール, 菜单控件. |
| Parameter | パラメーター | 参数 | |
| Parameter prefix | 接頭辞 | 参数前缀 | |
| Synced | 同期する | 同步 | Sent to the other players. |
| Local | ローカル | 本地 | Only on your own screen, not synced. |
| Saved | 保存する | 保存 | Kept when you change avatars or rejoin. |
| Default | 初期値 | 默认值 | The value a parameter or a toggle starts at. A short button meaning "back to its own default" is デフォルト in Japanese. |
| Default on | 初期状態オン | 默认开启 | A toggle that starts on. |
| On / Off | オン / オフ | 开启 / 关闭 | On the preset's narrow two way switch (`ad.ui.preset.on`, `ad.ui.preset.off`) Chinese uses 开 / 关, because 开启 / 关闭 does not fit there. |
| Bit | ビット | bits | The unit of VRChat's parameter memory: 256 per avatar. Chinese keeps the unit, as Modular Avatar does. |
| Synced bits | 同期ビット | 同步参数（bits） | "Costs no synced bits" is 不占用同步参数. |
| Upload | アップロード | 上传 | |
| Build | ビルド | 构建 | NDMF says 构建. |
| Play | 再生 | 播放 | Unity's Play button, which builds the avatar like an upload does. |
| Tracking | | | VRChat driving a part of the avatar itself (blinking, lip sync); both translations say it in words. |
| Blinking | まばたき | 眨眼 | ("Hold blink": まばたきを保持) |
| Lip sync | リップシンク | 口型同步 | |
| Viseme | 口パク | 口型 | A mouth shape for one sound, used by lip sync ("Hold visemes": 口パクを保持). |
| Face tracking | フェイストラッキング | 面部追踪 | Face and eye tracking hardware moving the face through a layer of the FX animator. |
| Gesture | ジェスチャー | 手势 | A VRChat hand gesture. The seven besides Neutral: Fist, Hand open, Point, Victory, Rock and roll, Hand gun, Thumbs up; グー、パー、指さし、ピース、ロックンロール、ハンドガン、サムズアップ; 握拳、张开手、指向、剪刀手、摇滚、手枪、点赞. |
| Eye | 目 | 眼睛 | |
| PC | PC | PC | The Windows version of VRChat. |
| Quest | Quest | Quest | The Android version of VRChat (Quest and phones). |
| Platform | プラットフォーム | 平台 | |
| Inspector | インスペクター | Inspector | Unity window. Chinese keeps the window names in English, as Modular Avatar does. |
| Hierarchy | ヒエラルキー | Hierarchy | Unity window. |
| Scene view | シーンビュー | Scene 视图 | Unity window. |
| Component | コンポーネント | 组件 | |
| GameObject | ゲームオブジェクト | 对象 | |
| Object | オブジェクト | 对象 | AvatarDoc's short word for a GameObject. |
| Prefab | プレハブ | 预制件 | |
| Transform | トランスフォーム | Transform | |
| Bone | ボーン | 骨骼 | A humanoid bone such as Head or Hand. |
| Hand | 手 | 手 | |
| Head | 頭 | 头 | The bone's own name, Head, stays in English in quotes. |
| Humanoid | Humanoid | Humanoid | Unity's rig type, kept. |
| Renderer | レンダラー | 渲染器 | |
| Mesh | メッシュ | 网格 | |
| Skinned mesh | Skinned Mesh | 蒙皮网格 | |
| Blend shape | ブレンドシェイプ | 形态键 | Settled: Modular Avatar's Japanese spelling, and what the Chinese community says. |
| Shape | シェイプ | 形态键 | The short form in labels. |
| Material | マテリアル | 材质 | |
| Material slot | マテリアルスロット | 材质槽 | |
| Shader | シェーダー | 着色器 | |
| Property | プロパティ | 属性 | A material property: a colour, a number or a vector of a shader. A shader property is シェーダープロパティ, 着色器属性. |
| Animation | アニメーション | 动画 | |
| Animation clip | クリップ | 动画剪辑 | Also 剪辑 in Chinese. |
| Animator | アニメーター | Animator | Chinese keeps it, as Modular Avatar does. |
| Animator Controller | コントローラー | 控制器 | |
| Layer | レイヤー | 层 | An animator layer. |
| FX layer | FXレイヤー | FX 层 | |
| State | ステート | 状态 | A state in an animator layer. |
| Transition | トランジション | 过渡 | |
| Blend tree | ブレンドツリー | 混合树 | |
| Curve | カーブ | 曲线 | One animated value in a clip. |
| Weight | ウェイト | 权重 | A blend shape's value from 0 to 100, or a layer's weight. |
| Constraint | コンストレイント | 约束 | |
| Asset | アセット | 资源 | |
| Icon | アイコン | 图标 | |
| Undo | 元に戻す | 撤销 | |
| Tag | コンタクトタグ | Contact 标签 | A contact's collision tag. |
| Collider | コライダー | 碰撞体 | What touches a contact. |
| Anchor | アンカー | 锚点 | |
| Radius | 半径 | 半径 | |
| Offset | オフセット | 偏移 | |
| Position | 位置 | 位置 | |
| Threshold | しきい値 | 幅度 | The amount a PhysBone event needs. |
| Grab | 掴まれた | 被抓取 | A PhysBone event. |
| Pose | ポーズ | 被摆放 | A PhysBone event: let go in a new pose. |
| Angle | 一定以上曲がる | 弯曲超过 | A PhysBone event. |
| Stretch | 一定以上伸びる | 拉伸超过 | A PhysBone event. |
| Squish | 一定以上潰れる | 挤压超过 | A PhysBone event. |
| Parameter Driver, Animator Layer Control | Parameter Driver, Animator Layer Control | Parameter Driver, Animator Layer Control | VRChat state behaviour names, kept. |

## AvatarDoc's own words

| English | Japanese | Simplified Chinese | Note |
|---|---|---|---|
| Entry | 項目 | 条目 | One toggle, radial, submenu or trigger made in AvatarDoc. Kept apart from a menu item. |
| Row | 行 | 行 | A row of the menu tree. |
| Effect | 効果 | 效果 | What an entry changes: objects, blend shapes, materials, properties, animations, follows. |
| Target | ターゲット | 目标 | |
| Trigger | トリガー | 触发器 | Effects that follow another control, a parameter, a blend shape, a contact or a PhysBone, with no menu button. |
| Override | オーバーライド | 覆盖 | Holds a blend shape at one value in every animation that uses it. |
| Preset | プリセット | 预设 | One button that puts several controls into one state. |
| Outfit | 衣装 | 服装 | |
| Outfit set | 衣装セット | 服装组 | |
| Piece | パーツ | 部件 | One object of an outfit. |
| Shrink shapes | 縮小シェイプ | 收缩形态键 | |
| Exclusive group | 排他グループ | 互斥组 | Only one member is on at a time. |
| Follow | 追従 | 跟随 | An object that follows another one while the entry is on. |
| Keep offset | オフセットを維持 | 保持偏移 | |
| Grabbable | 掴めるオブジェクト | 可抓取物 | An object you pick up and hold in a hand. |
| Merged animator | 統合アニメーター | 合并的 Animator | |
| Match write defaults | WDを合わせる | 匹配 Write Defaults | |
| Path root | パスのルート | 路径根对象 | |
| Menu asset | メニューアセット | 菜单资源 | An existing Expressions Menu installed into the menu. |
| Declare | 宣言 | 声明 | A parameter declared under Parameters. |
| Install into | インストール先 | 安装到 | Where AvatarDoc's entries land in the menu. |
| Install as | インストール方法 | 安装方式 | |
| Install path | インストールパス | 安装路径 | |
| As submenu / Append items | サブメニューとして / 直接追加 | 作为子菜单 / 追加菜单项 | |
| Simple | シンプル | 简易 | The mode that shows what most avatars need. |
| Advanced | 詳細 | 高级 | The mode that shows every tool. The Advanced fold is 詳細設定 in Japanese. |
| Component settings | コンポーネント設定 | 组件设置 | |
| Wheel | ホイール | 轮盘 | AvatarDoc's preview of the in game menu. |
| Wedge | 区画 | 扇区 | One part of the Wheel. |
| Summary | 概要 | 摘要 | |
| Lock / Unlock | ロック / ロック解除 | 锁定 / 解锁 | |
| Preview | シーンプレビュー / プレビュー | 场景预览 | The scene preview. |
| Test mode | テストモード | 测试模式 | |
| Convert | 変換 | 转换 | Turns a VRCFury, Modular Avatar or hand made toggle into AvatarDoc entries. |
| Remove originals | 元を削除 | 移除原件 | |
| When | 発動条件 | 触发条件 | The trigger panel's rule. |
| Condition | 条件 | 条件 | A condition of a trigger. |
| Condition (comparison) | 比較 | 比较方式 | The comparison of a parameter condition. |
| Group | グループ | 组 | A group of conditions. |
| Requirement | 必要条件 | 前提 | An extra condition a trigger also needs. |
| Card | カード | 卡片 | One condition, shown as a card. |
| + And / + Or | + かつ / + または | + 且 / + 或 | |
| Any of these / All of | いずれか / すべて | 满足任一组 / 全部满足 | |
| Only while | 有効条件 | 仅当 | The gate: a trigger works only while a switch is in a state. |
| Smooth | スムーズ | 平滑 | A blend shape trigger that fades with the shape. |
| Switch | スイッチ | 切换 | A blend shape trigger that turns on at one value. |
| Starts at / Fully on at / Switch at / Swaps at | 開始点 / 最大点 / 切替点 / 入替点 | 开始于 / 完全开启于 / 切换于 / 替换于 | The four weight points. |
| Add / Replace | 加算 / 置換 | 叠加 / 替换 | A trigger's mode. |
| Fixed / Pin | 一定 / ピン留め | 固定 / 钉住 | |
| Follow source | ソースに追従 | 跟随源 | |
| Capture | 取り込み | 捕获 | Taking a blend shape's current values. |
| Capture icon | アイコン撮影 | 拍摄图标 | |
| Hold the rest | 残りを保持 | 保持其余 | Keeps the other blend shapes of a mesh at their default or at 0. |
| Touched by | 接触対象 | 触碰来源 | |
| Who / Self / Others | 触れる人 / 自分 / 他プレイヤー | 触碰者 / 自己 / 他人 | |
| Touch / Closer than | 接触 / 近接 | 触碰 / 近于 | |
| Hold / Flip / Fade | ホールド / 切り替え / フェード | 保持 / 翻转 / 渐变 | A touch trigger's mode. |
| Contact starts | 定番コンタクト | Contact 模板 | Headpat, Eye touch, Nose boop: なでなで / 目タッチ / 鼻ツン in Japanese; Headpat is 摸头 in Chinese. |
| Test hand | テスト用の手 | 测试用手 | The sphere you drag in the Scene view in place of a hand, so the preview shows what contact triggers do. |
| Problem / Warning / Error / Info | 問題 / 警告 / エラー / 情報 | 问题 / 警告 / 错误 / 信息 | |
| Ready to upload | アップロード可能 | 可以上传 | |
| Blend time | ブレンド時間 | 过渡时间 | |
| Duplicate / Copy | 複製 / コピー | 创建副本 / 复制 | Kept apart in Chinese: 复制 is the clipboard. |
| Copy to another avatar | 別のアバターにコピー | 复制到另一个 Avatar | Copies one AvatarDoc component to another version of the avatar. |
| Pick by hand when copying | コピー時に手動で選ぶ | 复制时手动选择 | A copy to another avatar never looks for this object by its place: the creator picks the object to use there. |
| Memory bar | メモリーバー | 参数用量条 | Chinese avoids 内存条, a RAM stick. |
| Gesture faces | ジェスチャー表情 | 手势表情 | The window that gives each hand gesture a face, made of triggers. |
| Paused / Wins (while tracked) | 停止 / 優先（トラッキング中） | 暂停 / 优先（追踪时） | What a gesture face does while face tracking is on: nothing, or show over face tracking while held. |

## Kept in English

These stay in English in every language:

- AvatarDoc
- VRChat
- Unity
- NDMF
- Modular Avatar
- VRCFury
- PhysBone
- Contact (the VRChat contact system; Japanese writes コンタクト, as the community and Modular Avatar do)
- FX (the FX animator layer)
- Write Defaults
- AudioLink
- Other product names, such as Poiyomi, Gesture Manager and VCC (the VRChat Creator Companion)
- Layer, parameter, shader property and asset names: they are the creator's own names, usually in quotes
