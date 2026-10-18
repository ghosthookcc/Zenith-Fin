import { component$ } from '@builder.io/qwik';

const paths: Record<string, string> = {
    plane: 'm22 2-7 20-4-9-9-4 20-7ZM22 2 11 13',
    home: 'm3 10 9-7 9 7v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V10Zm6 11v-8h6v8',
    heart: 'M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.7l-1.1-1.1a5.5 5.5 0 0 0-7.8 7.8L12 21l8.8-8.6a5.5 5.5 0 0 0 0-7.8Z',
    wallet: 'M20 8V5a2 2 0 0 0-2-2H5a3 3 0 0 0 0 6h15a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H5a3 3 0 0 1-3-3V6m19 7h-5v4h5',
    shield: 'M12 3 3 7v5c0 5 9 9 9 9s9-4 9-9V7l-9-4Zm-4 9 3 3 5-6',
    layers: 'm12 3 10 5-10 5L2 8l10-5ZM2 12l10 5 10-5M2 16l10 5 10-5',
    eye: 'M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12Zm13 0a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z',
    eyeoff: 'm3 3 18 18M10.6 5.1A12 12 0 0 1 12 5c6.5 0 10 7 10 7a17 17 0 0 1-3.2 4M6.5 6.5A20 20 0 0 0 2 12s3.5 7 10 7a12 12 0 0 0 5.5-1.5M10 10a3 3 0 0 0 4 4',
    lock: 'M6 10V7a6 6 0 0 1 12 0v3M5 10h14v11H5V10Zm7 5v2',
    info: 'M12 11v6m0-10v.01M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0Z',
    bank: 'm3 9 9-6 9 6H3Zm2 4v6m7-6v6m7-6v6M3 22h18',
    calendar: 'M8 2v4m8-4v4M3 10h18M5 4h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2Z',
    chevronLeft: 'm15 18-6-6 6-6',
    chevronRight: 'm9 18 6-6-6-6',
    chevronDown: 'm6 9 6 6 6-6',
    refresh: 'M21 12a9 9 0 1 1-2.6-6.4L21 8m0-5v5h-5',
    cart: 'M3 4h2.2l2.3 11.1A1.5 1.5 0 0 0 9 16.3h8.2a1.5 1.5 0 0 0 1.5-1.1L20.5 8H6.1M10 20a1 1 0 1 1-2 0 1 1 0 0 1 2 0Zm8 0a1 1 0 1 1-2 0 1 1 0 0 1 2 0Z',
    utensils: 'M4.5 3v5.5a2.5 2.5 0 0 0 5 0V3M7 3v18M17.5 21V3c-2 1.2-3.5 4-3.5 8h3.5',
    bus: 'M5 17V7a3 3 0 0 1 3-3h8a3 3 0 0 1 3 3v10H5Zm0-5h14M8 17v3m8-3v3M8.5 14.5h.01m7 0h.01',
    bolt: 'M13 2 4.5 13.5H11L10 22l8.5-11.5H12L13 2Z',
    bag: 'M5.5 8h13l-1 13h-11l-1-13Zm3.5 0V6.5a3 3 0 0 1 6 0V8',
    film: 'M4 6.5A2.5 2.5 0 0 1 6.5 4h11A2.5 2.5 0 0 1 20 6.5v11a2.5 2.5 0 0 1-2.5 2.5h-11A2.5 2.5 0 0 1 4 17.5v-11Zm6.5 2.8v5.4l4.5-2.7-4.5-2.7Z',
    pulse: 'M3 12h4l2.5-6 5 12 2.5-6h4',
    trend: 'm3 17 6-6 4 4 8-8m0 0h-5m5 0v5',
    receipt: 'M6 3h12v18l-3-2-3 2-3-2-3 2V3Zm3.5 5h5m-5 4h5m-5 4h3',
    income: 'M12 3v11m0 0-4-4m4 4 4-4M4 15v3a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-3',
    transfer: 'M4 8h14m0 0-4-4m4 4-4 4m6 4H6m0 0 4-4m-4 4 4 4',
    tag: 'M3.5 12.3V4.5a1 1 0 0 1 1-1h7.8l8.2 8.2-8.8 8.8-8.2-8.2ZM9 8a1 1 0 1 1-2 0 1 1 0 0 1 2 0Z',
};

export const Icon = component$<{ name: string; class?: string }>(({ name, class: className }) => (
    <svg class={className || ''} width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.65" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d={paths[name] || paths.wallet} /></svg>
));