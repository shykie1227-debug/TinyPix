import { Film, Settings, ArrowDownToLine, Scissors, ArrowRightLeft, Music, ImageDown } from 'lucide-react';

type Nav = 'home' | 'workspace';
type WorkspaceTab = 'image' | 'video' | 'gif' | 'trim' | 'convert' | 'audio';

interface SidebarProps {
  activeNav: Nav;
  onNavChange: (nav: Nav) => void;
  activeTab?: WorkspaceTab;
  onTabChange?: (tab: WorkspaceTab) => void;
  onOpenSettings?: () => void;
}

export default function Sidebar({ activeNav, onNavChange, activeTab, onTabChange, onOpenSettings }: SidebarProps) {
  const isImageMode = activeTab === 'image';
  const subtitle = isImageMode ? '专业图像工具集' : '批量处理引擎';

  return (<aside className="w-64 shrink-0 flex flex-col h-screen py-8 border-r border-outline-variant/20 bg-surface-container-low">
    <div className="px-6 mb-12">
      <h1 className="text-on-surface font-semibold text-2xl leading-8">
        TinyPix Pro
      </h1>
      <p className="text-on-surface-variant tracking-wider mt-1 uppercase font-mono text-[11px] leading-4 tracking-[0.1em] font-medium opacity-60">
        {subtitle}
      </p>
    </div>

    <nav className="flex-grow space-y-1 px-3 overflow-y-auto">
      {isImageMode ? (
        <div>
          <p className="px-6 mb-2 text-on-surface-variant font-label-caps opacity-50 mt-1">
            图片工具
          </p>
          <button onClick={() => { onNavChange('workspace'); onTabChange?.('image'); }} className={`w-full flex items-center gap-3 px-4 py-3 mx-2 rounded-full transition-opacity ${activeNav === 'workspace' ? 'bg-secondary-container text-on-secondary-container font-semibold' : 'text-on-surface-variant hover:opacity-60'}`}>
            <ImageDown size={24}/>
            <span className="flex-1 text-left text-sm font-semibold">
              图片导出
            </span>
          </button>
        </div>
      ) : (
      <div>
        <p className="px-6 mb-2 text-on-surface-variant font-label-caps opacity-50 mt-1">
          视频工具
        </p>
        <div className="space-y-1">
          {[
            { id: 'video' as WorkspaceTab, label: '视频压缩', icon: ArrowDownToLine },
            { id: 'gif' as WorkspaceTab, label: '视频转 GIF', icon: Film },
            { id: 'convert' as WorkspaceTab, label: '视频格式转换', icon: ArrowRightLeft },
            { id: 'trim' as WorkspaceTab, label: '视频剪辑', icon: Scissors },
            { id: 'audio' as WorkspaceTab, label: '提取音频', icon: Music },
          ].map((item) => {
            const Icon = item.icon;
            const isActive = activeNav === 'workspace' && activeTab === item.id;
            return (<button key={item.id} onClick={() => { onNavChange('workspace'); onTabChange?.(item.id); }} className={`w-full flex items-center gap-3 px-4 py-3 mx-2 rounded-full transition-opacity ${isActive ? 'bg-secondary-container text-on-secondary-container font-semibold' : 'text-on-surface-variant hover:opacity-60'}`}>
              <Icon size={24}/>
              <span className="flex-1 text-left text-sm font-semibold">
                {item.label}
              </span>
            </button>);
          })}
        </div>
      </div>
      )}
    </nav>

    <div className="mt-auto px-3 space-y-1">
      <div className="border-t border-outline-variant/20 mb-3"/>
      <button onClick={onOpenSettings} className="w-full flex items-center gap-3 px-4 py-3 mx-2 rounded-full transition-opacity text-on-surface-variant hover:opacity-60">
        <Settings size={24}/>
        <span className="text-sm font-semibold">设置</span>
      </button>
    </div>
  </aside>);
}
