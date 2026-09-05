import { Component, input } from '@angular/core';

@Component({
  selector: 'app-icon',
  standalone: true,
  template: `
    <span
      class="material-symbols-rounded select-none leading-none inline-flex items-center justify-center align-middle"
      [class.filled]="filled()"
      [style.font-size.px]="size()"
      [style.width.px]="size()"
      [style.height.px]="size()"
    >
      {{ name() }}
    </span>
  `,
})
export class IconComponent {
  readonly name = input.required<string>();
  readonly size = input<number>(20);
  readonly filled = input<boolean>(false);
}
