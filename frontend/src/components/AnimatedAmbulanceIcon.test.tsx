import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { AnimatedAmbulanceIcon } from './AnimatedAmbulanceIcon';

describe('<AnimatedAmbulanceIcon />', () => {
  it('renders an accessible animated ambulance SVG with body wheels and cross', () => {
    render(<AnimatedAmbulanceIcon />);

    const icon = screen.getByRole('img', { name: /ambulance/i });
    expect(icon).toHaveClass('animated-ambulance');
    expect(icon).toHaveClass('animate-ambulance-float');
    expect(screen.getByTestId('ambulance-body')).toBeInTheDocument();
    expect(screen.getByTestId('ambulance-wheel-left')).toBeInTheDocument();
    expect(screen.getByTestId('ambulance-wheel-right')).toBeInTheDocument();
    expect(screen.getByTestId('ambulance-cross')).toBeInTheDocument();
  });
});