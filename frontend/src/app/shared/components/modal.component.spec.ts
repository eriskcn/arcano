import { TestBed } from '@angular/core/testing';
import { ModalComponent } from './modal.component';

describe('ModalComponent', () => {
  const create = (open: boolean) => {
    const fixture = TestBed.createComponent(ModalComponent);
    fixture.componentRef.setInput('open', open);
    fixture.detectChanges();
    const closed = vi.fn();
    fixture.componentInstance.close.subscribe(closed);
    return closed;
  };

  it('emits close on Escape while open', () => {
    const closed = create(true);
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(closed).toHaveBeenCalledTimes(1);
  });

  it('ignores Escape while closed', () => {
    const closed = create(false);
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(closed).not.toHaveBeenCalled();
  });
});
