# 人机对弈侧栏组件

正式素材采用游戏原有胡桃木、宣纸与金属收边质感；头像按用户确认采用水墨侧颜，玩家朝右、电脑隐士朝左。头像、圆框、执棋标记分别叠加。

新增素材：
- players-card-v1.png：双方公共信息卡底图。
- avatar-player-ink-profile-v5.png：玩家水墨侧颜，已确认。
- avatar-computer-ink-sage-v3.png：面朝左侧的水墨隐士。
- avatar-frame-v1.png：双方共用圆框，中心透明。
- button-analysis-v1.png：形势判断、数目共用青瓷色底图。

侧栏底板、记录卡、普通按钮、认输按钮及黑白棋子复用原项目资源，路径见 components.json。普通底图共用于悔棋、停一手、和棋。所有文字、动态数值和右上角读秒次数由界面叠加。

使用内置 image_gen 生成。完整最终提示词、复用路径和布局说明保存在 components.json。GoGame.AiArt.cs 已接入运行界面，桌面、棋盒、棋盖的运行时副本也位于本目录。avatar-computer-ink-v2.png 为早先机器人备选，不用于当前方案。
