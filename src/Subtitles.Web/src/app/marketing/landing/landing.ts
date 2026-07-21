import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Auth } from '../../core/auth/auth';

interface Feature {
  icon: string;
  title: string;
  description: string;
}

interface Step {
  number: string;
  title: string;
  description: string;
}

@Component({
  selector: 'app-landing',
  imports: [RouterLink, MatButtonModule, MatIconModule],
  templateUrl: './landing.html',
  styleUrl: './landing.scss',
})
export class Landing {
  protected readonly auth = inject(Auth);

  protected readonly features: Feature[] = [
    {
      icon: 'language',
      title: 'Automatic language detection',
      description:
        'Upload in any supported spoken language — Telugu, Hindi, and more — with no manual language selection to get started.',
    },
    {
      icon: 'auto_awesome',
      title: 'AI transcription & cleanup',
      description:
        'Speech-to-text plus an LLM cleanup pass turn raw audio into natural, correctly punctuated subtitle lines, not robotic captions.',
    },
    {
      icon: 'translate',
      title: 'Three outputs, one upload',
      description:
        'Get native-script, English-translation, and romanized-transliteration subtitles from a single pass — switch between them anytime.',
    },
    {
      icon: 'highlight',
      title: 'Smart word highlighting',
      description:
        'Important words are highlighted automatically. Add or remove highlights by hand — your edits always win over the automatic pass.',
    },
  ];

  protected readonly steps: Step[] = [
    { number: '01', title: 'Upload', description: 'Drop in your video. No format wrangling required.' },
    {
      number: '02',
      title: 'Detect & transcribe',
      description: 'We identify the spoken language and generate a clean, natural transcript.',
    },
    {
      number: '03',
      title: 'Choose your output',
      description: 'Native script, English translation, or romanized — or all three.',
    },
    {
      number: '04',
      title: 'Edit & export',
      description: 'Fine-tune highlights and wording, then export subtitles or a burned-in video.',
    },
  ];
}
