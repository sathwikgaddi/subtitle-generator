import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { VideoList } from './video-list';

describe('VideoList', () => {
  let component: VideoList;
  let fixture: ComponentFixture<VideoList>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [VideoList],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(VideoList);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
