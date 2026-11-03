import { TestBed } from '@angular/core/testing';
import { ContextMenuComponent, ContextMenuModel } from './context-menu.component';
import { FileItem, MenuEntry } from './app';

const file = (id: string): FileItem => ({ id, name: `File ${id}`, type: 'document', size: '1 KB', modified: '', starred: false });

describe('ContextMenuComponent', () => {
  const create = (patch: Partial<ContextMenuModel> = {}, entries?: MenuEntry[]) => {
    const fixture = TestBed.createComponent(ContextMenuComponent);
    const run = vi.fn();
    const menu: ContextMenuModel = {
      title: 'File 1',
      items: [file('1')],
      x: 10,
      y: 10,
      entries: entries ?? [
        { label: 'Preview', icon: 'visibility', run },
        { label: 'Move to…', icon: 'drive_file_move', submenu: true, run: () => {} },
        { label: 'Off', icon: 'block', disabled: true, run },
        { label: 'Move to trash', icon: 'delete', danger: true, divider: true, run },
      ],
      ...patch,
    };
    fixture.componentRef.setInput('menu', menu);
    fixture.componentRef.setInput('moveTargets', [{ id: null, name: 'My files' }, { id: '1', name: 'Self' }, { id: 'd1', name: 'Docs' }]);
    const action = vi.fn();
    const move = vi.fn();
    fixture.componentInstance.action.subscribe(action);
    fixture.componentInstance.move.subscribe(move);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const buttons = () => Array.from(el.querySelectorAll<HTMLElement>('[role="menuitem"]'));
    return { fixture, el, buttons, run, action, move };
  };

  it('keeps the menu within the viewport', () => {
    const { el } = create({ x: window.innerWidth, y: window.innerHeight });
    const menu = el.querySelector<HTMLElement>('[role="menu"]')!;
    const rows = el.querySelectorAll('[role="menuitem"]').length;
    const separators = el.querySelectorAll('[role="separator"]').length;

    expect(menu.style.left).toBe(`${Math.max(8, window.innerWidth - 248)}px`);
    // 8px padding, 36px per row, 9px per separator.
    expect(menu.style.top).toBe(`${Math.max(8, window.innerHeight - (8 + rows * 36 + separators * 9) - 8)}px`);
  });

  it('emits the chosen entry, but not a disabled one', () => {
    const { buttons, action } = create();

    buttons()[2].click();
    expect(action).not.toHaveBeenCalled();
    buttons()[0].click();
    expect(action).toHaveBeenCalledWith(expect.objectContaining({ label: 'Preview' }));
  });

  it('opens the move list without the selected items and emits the target', () => {
    const { fixture, buttons, action, move } = create();

    buttons()[1].click();
    fixture.detectChanges();

    expect(action).not.toHaveBeenCalled();
    expect(buttons().map(b => b.textContent!.trim())).toEqual(['folderMy files', 'folderDocs']);
    buttons()[1].click();
    expect(move).toHaveBeenCalledWith({ items: [file('1')], targetId: 'd1' });
  });

  it('moves focus with the arrow keys and skips disabled entries', () => {
    const { el, buttons } = create();
    document.body.appendChild(el);
    const menu = el.querySelector<HTMLElement>('[role="menu"]')!;
    const press = (key: string) => menu.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));

    press('ArrowDown');
    expect(document.activeElement).toBe(buttons()[0]);
    press('ArrowDown');
    press('ArrowDown');
    expect(document.activeElement).toBe(buttons()[3]);
    press('ArrowDown');
    expect(document.activeElement).toBe(buttons()[0]);
    press('ArrowUp');
    expect(document.activeElement).toBe(buttons()[3]);
    el.remove();
  });
});
