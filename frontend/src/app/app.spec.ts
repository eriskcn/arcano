import { TestBed } from '@angular/core/testing';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render welcome back heading', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Welcome back');
  });

  it('should show a file context menu within the viewport', async () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const file = app.files()[0];
    const event = new MouseEvent('contextmenu', {
      clientX: window.innerWidth,
      clientY: window.innerHeight,
      cancelable: true,
    });

    app.page.set('app');
    app.showContextMenu(file, event);
    fixture.detectChanges();
    await fixture.whenStable();

    const menu = fixture.nativeElement.querySelector('[role="menu"]') as HTMLElement | null;
    expect(event.defaultPrevented).toBe(true);
    expect(menu?.textContent).toContain('Show details');
    expect(menu?.style.left).toBe(`${Math.max(8, window.innerWidth - 200)}px`);
    expect(menu?.style.top).toBe(`${Math.max(8, window.innerHeight - 88)}px`);
  });

  it('should toggle a file favorite', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const file = app.files()[0];
    const initialStarredState = file.starred;

    app.toggleFileStar(file.id);

    expect(app.files()[0].starred).toBe(!initialStarredState);
  });

  it('should support single, toggle, and range selection', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;

    app.selectFile('2');
    expect([...app.selectedIds()]).toEqual(['2']);

    app.selectFile('6', new MouseEvent('click', { shiftKey: true }));
    expect([...app.selectedIds()]).toEqual(['2', '3', '4', '5', '6']);

    app.selectFile('4', new MouseEvent('click', { ctrlKey: true }));
    expect([...app.selectedIds()]).toEqual(['2', '3', '5', '6']);
  });

  it('should clear selection on Escape', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;

    app.selectFile('3');
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

    expect(app.selectedIds().size).toBe(0);
    expect(app.selectedId()).toBeNull();
  });

  it('should select files intersected by a marquee', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    const surface = document.createElement('div');
    surface.setPointerCapture = () => {};
    surface.hasPointerCapture = () => false;
    const first = document.createElement('div');
    first.dataset['fileId'] = '1';
    first.getBoundingClientRect = () => ({ left: 10, top: 10, right: 30, bottom: 30 } as DOMRect);
    const second = document.createElement('div');
    second.dataset['fileId'] = '2';
    second.getBoundingClientRect = () => ({ left: 40, top: 40, right: 60, bottom: 60 } as DOMRect);
    surface.append(first, second);

    const pointer = (type: string, x: number, y: number): PointerEvent => {
      const event = Object.assign(new MouseEvent(type, { clientX: x, clientY: y, bubbles: true }), { pointerId: 1 });
      Object.defineProperty(event, 'currentTarget', { value: surface });
      return event as PointerEvent;
    };

    app.beginMarquee(pointer('pointerdown', 0, 0));
    app.moveMarquee(pointer('pointermove', 35, 35));

    expect([...app.selectedIds()]).toEqual(['1']);
    expect(app.selectionRect()).toEqual({ left: 0, top: 0, width: 35, height: 35 });

    app.endMarquee(pointer('pointerup', 35, 35));
    expect(app.selectionRect()).toBeNull();
  });
});
