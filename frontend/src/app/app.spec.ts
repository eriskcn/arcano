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
});
