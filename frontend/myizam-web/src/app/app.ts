import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { LangService, UiLang } from './services/lang';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly lang = inject(LangService);
  protected readonly langs: UiLang[] = ['ky', 'ru', 'en'];
  protected readonly langLabels: Record<UiLang, string> = { ky: 'КЫР', ru: 'РУС', en: 'ENG' };

  constructor() {
    this.lang.init();
  }
}
