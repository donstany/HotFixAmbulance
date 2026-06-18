type AnimatedAmbulanceIconProps = {
  className?: string;
};

export function AnimatedAmbulanceIcon({ className = '' }: AnimatedAmbulanceIconProps) {
  const classes = ['animated-ambulance', className].filter(Boolean).join(' ');

  return (
    <svg
      role="img"
      aria-label="Animated ambulance icon"
      viewBox="0 0 88 56"
      className={`${classes} animate-ambulance-float`}
      width="88"
      height="56"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <title>Animated ambulance icon</title>
      <defs>
        <linearGradient id="ambulance-body" x1="8" y1="14" x2="78" y2="44" gradientUnits="userSpaceOnUse">
          <stop stopColor="#F9FDFF" />
          <stop offset="0.55" stopColor="#DCEFFE" />
          <stop offset="1" stopColor="#9ED8FB" />
        </linearGradient>
        <linearGradient id="ambulance-cab" x1="50" y1="16" x2="84" y2="40" gradientUnits="userSpaceOnUse">
          <stop stopColor="#F8FAFC" />
          <stop offset="0.58" stopColor="#D9ECFF" />
          <stop offset="1" stopColor="#87CCF5" />
        </linearGradient>
        <linearGradient id="ambulance-window" x1="47" y1="16" x2="71" y2="30" gradientUnits="userSpaceOnUse">
          <stop stopColor="#DDF4FF" />
          <stop offset="1" stopColor="#B9E9FF" />
        </linearGradient>
        <linearGradient id="ambulance-red" x1="0" y1="0" x2="1" y2="1">
          <stop stopColor="#FB7185" />
          <stop offset="1" stopColor="#BE123C" />
        </linearGradient>
        <linearGradient id="ambulance-chrome" x1="58" y1="20" x2="81" y2="36" gradientUnits="userSpaceOnUse">
          <stop stopColor="#F8FAFC" />
          <stop offset="0.45" stopColor="#CBD5E1" />
          <stop offset="1" stopColor="#475569" />
        </linearGradient>
        <linearGradient id="ambulance-window-dark" x1="50" y1="17" x2="76" y2="31" gradientUnits="userSpaceOnUse">
          <stop stopColor="#1E293B" />
          <stop offset="1" stopColor="#0F172A" />
        </linearGradient>
        <linearGradient id="wheel-gradient" x1="22" y1="34" x2="22" y2="52" gradientUnits="userSpaceOnUse">
          <stop stopColor="#334155" />
          <stop offset="1" stopColor="#0F172A" />
        </linearGradient>
        <filter id="ambulance-shadow" x="0" y="0" width="88" height="56" filterUnits="userSpaceOnUse" colorInterpolationFilters="sRGB">
          <feDropShadow dx="0" dy="4" stdDeviation="3" floodColor="#0F172A" floodOpacity="0.18" />
        </filter>
      </defs>
      <g filter="url(#ambulance-shadow)">
        <g>
          <path
            d="M10.5 22.6C10.5 19.3 13.1 16.7 16.4 16.7H55.2C58.1 16.7 60.9 17.7 63.1 19.6L69.3 24.8C70.7 26 72.5 26.7 74.3 26.7H79.8C81.2 26.7 82.5 27.2 83.4 28.2C84.3 29.2 84.8 30.5 84.8 31.9V37.7H10.5V22.6Z"
            fill="url(#ambulance-body)"
            stroke="#0F172A"
            strokeOpacity="0.08"
            data-testid="ambulance-body"
          />
          <path
            d="M49.8 16.9H64.9C67.4 16.9 69.7 17.9 71.4 19.8L78.4 27.5C79.2 28.4 79.7 29.5 79.7 30.7V37.7H49.8V16.9Z"
            fill="url(#ambulance-cab)"
            stroke="#0F172A"
            strokeOpacity="0.08"
          />
          <path
            d="M53.4 21.9H61.2C63.1 21.9 64.7 22.6 66 23.9L69.7 28.1H53.4V21.9Z"
            fill="url(#ambulance-window-dark)"
          />
          <path
            d="M64.9 22.9H68.6C69.8 22.9 70.8 23.4 71.6 24.3L75.6 28.9H66.5L64.9 22.9Z"
            fill="#E2F4FF"
          />
          <path d="M14.1 23.6H47.4" stroke="url(#ambulance-red)" strokeWidth="3.4" strokeLinecap="round" />
          <path d="M14.1 28.2H47.4" stroke="#FFFFFF" strokeOpacity="0.58" strokeWidth="1.8" strokeLinecap="round" />
          <path
            d="M38 17H44V12H49V17H54V22H49V27H44V22H38V17Z"
            fill="url(#ambulance-red)"
            className="animate-ambulance-beacon"
            data-testid="ambulance-cross"
          />
          <path d="M56.8 28.4H73.7V34.9H56.8V28.4Z" fill="#F8FDFF" />
          <path d="M58.2 29.7H72.2V33.6H58.2V29.7Z" fill="#DCEFFF" />
          <path d="M74.8 27.8H81.6V35.1H74.8V27.8Z" fill="url(#ambulance-chrome)" />
          <path d="M76 29.1H80.6V34H76V29.1Z" fill="#0F172A" fillOpacity="0.14" />
          <path d="M76.8 30.1H79.6" stroke="#E2E8F0" strokeOpacity="0.85" strokeWidth="1.2" strokeLinecap="round" />
          <path d="M76.8 32.1H79.6" stroke="#E2E8F0" strokeOpacity="0.85" strokeWidth="1.2" strokeLinecap="round" />
          <circle cx="74.3" cy="31.4" r="1.5" fill="#FDE68A" className="animate-ambulance-beacon" />
          <circle cx="82.3" cy="31.4" r="1.5" fill="#FDE68A" className="animate-ambulance-beacon" />
          <path d="M73.9 27.9H82.1" stroke="#94A3B8" strokeOpacity="0.45" strokeWidth="0.9" strokeLinecap="round" />
          <path d="M15.3 35.6H74.2" stroke="#0F172A" strokeOpacity="0.18" strokeWidth="2.2" strokeLinecap="round" />
          <path d="M18.4 30.9H43.4" stroke="#0F172A" strokeOpacity="0.12" strokeWidth="2" strokeLinecap="round" />
          <path d="M50.5 30.9H75.8" stroke="#0F172A" strokeOpacity="0.12" strokeWidth="2" strokeLinecap="round" />
          <path d="M72.2 28.9H79.1" stroke="#0F172A" strokeOpacity="0.12" strokeWidth="1.8" strokeLinecap="round" />
          <path d="M69.6 34.2H76.7" stroke="#0F172A" strokeOpacity="0.16" strokeWidth="1.9" strokeLinecap="round" />
          <path d="M76.6 24.4H82.4" stroke="#0F172A" strokeOpacity="0.14" strokeWidth="1.8" strokeLinecap="round" />
          <path d="M14.8 34.1H18.2" stroke="#0F172A" strokeOpacity="0.16" strokeWidth="2" strokeLinecap="round" />
          <path d="M16.4 31.2H24.4" stroke="#0F172A" strokeOpacity="0.08" strokeWidth="1.8" strokeLinecap="round" />
          <path d="M13.8 36.9C13.5 35.6 13.1 34.1 13.1 32.4" stroke="#0F172A" strokeOpacity="0.08" strokeWidth="2" strokeLinecap="round" />
          <path d="M54.7 24.2H63.6" stroke="#0F172A" strokeOpacity="0.12" strokeWidth="1.8" strokeLinecap="round" />
          <path d="M18.8 18C21.2 16.7 23.9 16.1 27 16.1H56.6" stroke="#FFFFFF" strokeOpacity="0.46" strokeWidth="2" strokeLinecap="round" />
          <path d="M11.4 22.6C12.9 20.4 15.1 19.1 17.7 18.8" stroke="#FFFFFF" strokeOpacity="0.55" strokeWidth="1.8" strokeLinecap="round" />
          <path d="M12.6 18.4H30.1" stroke="#0F172A" strokeOpacity="0.08" strokeWidth="1.5" strokeLinecap="round" />
          <path d="M23.1 17.4H44.3" stroke="#0F172A" strokeOpacity="0.06" strokeWidth="1.4" strokeLinecap="round" />
          <path d="M54.1 18.6H66.2" stroke="#0F172A" strokeOpacity="0.08" strokeWidth="1.5" strokeLinecap="round" />
          <path d="M62.1 21.9H67.2" stroke="#0F172A" strokeOpacity="0.1" strokeWidth="1.4" strokeLinecap="round" />
          <path d="M10.9 24.8C11.6 24.1 12.4 23.7 13.3 23.7H16.2" stroke="#FFFFFF" strokeOpacity="0.25" strokeWidth="1.3" strokeLinecap="round" />
          <g className="animate-ambulance-wheel-left animate-ambulance-wheel-origin" data-testid="ambulance-wheel-left">
            <circle cx="25.8" cy="39.9" r="8.6" fill="url(#wheel-gradient)" />
            <circle cx="25.8" cy="39.9" r="4.2" fill="#CBD5E1" />
            <circle cx="25.8" cy="39.9" r="1.2" fill="#334155" />
          </g>
          <g className="animate-ambulance-wheel-right animate-ambulance-wheel-origin" data-testid="ambulance-wheel-right">
            <circle cx="64.1" cy="39.9" r="8.6" fill="url(#wheel-gradient)" />
            <circle cx="64.1" cy="39.9" r="4.2" fill="#CBD5E1" />
            <circle cx="64.1" cy="39.9" r="1.2" fill="#334155" />
          </g>
          <path d="M18.9 36H73.3" stroke="#0F172A" strokeOpacity="0.3" strokeWidth="2.4" strokeLinecap="round" />
          <path d="M18.1 39.7H19.8" stroke="#0F172A" strokeOpacity="0.16" strokeWidth="2" strokeLinecap="round" />
          <path d="M68.7 39.7H70.4" stroke="#0F172A" strokeOpacity="0.16" strokeWidth="2" strokeLinecap="round" />
          <path d="M47.8 24.5H50.9" stroke="#0F172A" strokeOpacity="0.1" strokeWidth="1.8" strokeLinecap="round" />
        </g>
      </g>
    </svg>
  );
}
