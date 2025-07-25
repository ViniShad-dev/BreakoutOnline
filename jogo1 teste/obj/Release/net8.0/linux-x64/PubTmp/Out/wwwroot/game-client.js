// ======================================================
// CLIENT-SIDE MULTIPLAYER GAME LOGIC (V9 - NOVO VISUAL)
// ======================================================

const GameClient = {
    ctx: null,
    canvas: null,
    p1ScoreEl: null,
    p2ScoreEl: null,
    timerEl: null,
    p1EnergyBarEl: null,
    p2EnergyBarEl: null,
    scoreGoalEl: null,
    scoreLimit: 0,

    serverGameState: null,
    keys: {},

    powerUpIcons: {
        0: '↔️',
        1: '⚡',
        2: '👻',
        3: '🤏',
        4: '🌀'
    },

    paddleTrails: { 1: [], 2: [] },
    ballTrail: [],

    initialize(uiElements) {
        this.canvas = uiElements.canvas;
        this.ctx = this.canvas.getContext('2d');
        this.p1ScoreEl = uiElements.p1Score;
        this.p2ScoreEl = uiElements.p2Score;
        this.timerEl = uiElements.timer;
        this.p1EnergyBarEl = uiElements.p1Energy;
        this.p2EnergyBarEl = uiElements.p2Energy;
        this.scoreGoalEl = uiElements.scoreGoal;

        document.addEventListener("keydown", e => {
            this.keys[e.key.toLowerCase()] = true;
        });

        document.addEventListener("keyup", e => {
            this.keys[e.key.toLowerCase()] = false;
        });
    },

    setScoreLimit(limit) {
        this.scoreLimit = limit;
        if (this.scoreGoalEl) {
            this.scoreGoalEl.innerText = `Meta: ${this.scoreLimit}`;
        }
    },

    updateGameState(newState) {
        this.serverGameState = newState;
    },

    getInput() {
        let dx = 0;
        if (this.keys['arrowleft'] || this.keys['a']) dx = -1;
        if (this.keys['arrowright'] || this.keys['d']) dx = 1;

        const isDashing = this.keys['shift'] || false;

        return { dx, isDashing };
    },

    drawTrail(trail, isBall = false) {
        for (let i = 0; i < trail.length; i++) {
            const particle = trail[i];
            const opacity = 1 - (particle.life / particle.maxLife);

            if (isBall) {
                this.ctx.beginPath();
                this.ctx.arc(particle.x, particle.y, particle.radius * opacity, 0, Math.PI * 2);
                this.ctx.fillStyle = `rgba(243, 156, 18, ${opacity * 0.5})`; // Cor Laranja do Tema
                this.ctx.fill();
            } else {
                this.ctx.fillStyle = `rgba(0, 123, 255, ${opacity * 0.3})`; // Cor Azul do Tema
                this.ctx.fillRect(particle.x, particle.y, particle.width, particle.height);
            }

            particle.life++;
        }
        return trail.filter(p => p.life < p.maxLife);
    },

    draw() {
        if (!this.ctx) return;
        if (!this.serverGameState || !this.serverGameState.paddles) {
            this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
            return;
        }

        this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
        const { paddles, ball, score, timeRemaining, activePowerUps, countdown } = this.serverGameState;
        const p1 = paddles[1], p2 = paddles[2];

        this.paddleTrails[1] = this.drawTrail(this.paddleTrails[1]);
        this.paddleTrails[2] = this.drawTrail(this.paddleTrails[2]);
        this.ballTrail = this.drawTrail(this.ballTrail, true);

        if (p1) {
            if (p1.isDashing) {
                this.paddleTrails[1].push({ x: p1.x, y: p1.y, width: p1.width, height: p1.height, life: 0, maxLife: 15 });
            } else {
                this.paddleTrails[1] = [];
            }
            this.ctx.fillStyle = "#007BFF"; // Azul do Tema
            this.ctx.fillRect(p1.x, p1.y, p1.width, p1.height);
        }

        if (p2) {
            if (p2.isDashing) {
                this.paddleTrails[2].push({ x: p2.x, y: p2.y, width: p2.width, height: p2.height, life: 0, maxLife: 15 });
            } else {
                this.paddleTrails[2] = [];
            }
            this.ctx.fillStyle = "#E74C3C"; // Vermelho do Tema
            this.ctx.fillRect(p2.x, p2.y, p2.width, p2.height);
        }

        if (ball) {
            let ballColor = "#2ECC71"; // Verde do Tema
            if (ball.isFast) {
                ballColor = "#F39C12"; // Laranja do Tema
                this.ballTrail.push({ x: ball.x, y: ball.y, radius: ball.radius, life: 0, maxLife: 20 });
            } else {
                this.ballTrail = [];
            }

            if (!ball.isInvisible) {
                this.ctx.beginPath();
                this.ctx.arc(ball.x, ball.y, ball.radius, 0, Math.PI * 2);
                this.ctx.fillStyle = ballColor;
                this.ctx.fill();
                this.ctx.closePath();
            }
        }

        if (activePowerUps) {
            this.ctx.font = '24px Arial';
            this.ctx.textAlign = 'center';
            this.ctx.textBaseline = 'middle';
            activePowerUps.forEach(p => {
                const icon = this.powerUpIcons[p.type] || '?';
                this.ctx.fillText(icon, p.x, p.y);
            });
        }

        if (countdown > 0) {
            this.ctx.fillStyle = "rgba(234, 234, 234, 0.8)";
            this.ctx.font = "80px Arial";
            this.ctx.textAlign = "center";
            this.ctx.fillText(countdown, this.canvas.width / 2, this.canvas.height / 2 + 30);
        }

        this.p1ScoreEl.innerText = score[1] || 0;
        this.p2ScoreEl.innerText = score[2] || 0;

        if (timeRemaining >= 0) {
            const minutes = Math.floor(timeRemaining / 60);
            const seconds = Math.floor(timeRemaining % 60);
            this.timerEl.innerText = `Tempo: ${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
        }

        if (p1) this.p1EnergyBarEl.style.width = `${p1.energy}%`;
        if (p2) this.p2EnergyBarEl.style.width = `${p2.energy}%`;
    }
};