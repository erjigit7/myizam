import { Injectable, inject, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

export type UiLang = 'ky' | 'ru' | 'en';

/** Стартовый язык: localStorage → navigator.language (§9); язык UI ≠ язык вопроса. */
@Injectable({ providedIn: 'root' })
export class LangService {
  private readonly translate = inject(TranslateService);
  readonly current = signal<UiLang>('ru');

  init(): void {
    const saved = localStorage.getItem('myizam-lang') as UiLang | null;
    const lang = saved ?? this.fromNavigator();
    this.set(lang);
  }

  set(lang: UiLang): void {
    this.current.set(lang);
    this.translate.use(lang);
    localStorage.setItem('myizam-lang', lang);
    document.documentElement.lang = lang;
  }

  private fromNavigator(): UiLang {
    const nav = (navigator.language || '').toLowerCase();
    if (nav.startsWith('ky')) return 'ky';
    if (nav.startsWith('ru')) return 'ru';
    return 'en';
  }
}
